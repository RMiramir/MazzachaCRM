using System.Collections.Concurrent;
using FluentValidation;
using MazzachaCRM.Application.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace MazzachaCRM.Application.Common;

/// <summary>
/// Лёгкий диспетчер CQRS: находит обработчик по типу сообщения, перед ним запускает
/// валидаторы FluentValidation, а после команды сохраняет изменения одной транзакцией.
/// </summary>
public sealed class Dispatcher(IServiceProvider services, IUnitOfWork unitOfWork) : IDispatcher
{
    private static readonly ConcurrentDictionary<Type, object> Invokers = new();

    public async Task<TResult> Send<TResult>(ICommand<TResult> command, CancellationToken ct = default)
    {
        var invoker = (CommandInvoker<TResult>)Invokers.GetOrAdd(
            command.GetType(),
            static type => Activator.CreateInstance(typeof(CommandInvoker<,>).MakeGenericType(type, typeof(TResult)))!);

        var result = await invoker.Invoke(services, command, ct);
        await unitOfWork.SaveChangesAsync(ct);
        return result;
    }

    public Task<TResult> Query<TResult>(IQuery<TResult> query, CancellationToken ct = default)
    {
        var invoker = (QueryInvoker<TResult>)Invokers.GetOrAdd(
            query.GetType(),
            static type => Activator.CreateInstance(typeof(QueryInvoker<,>).MakeGenericType(type, typeof(TResult)))!);

        return invoker.Invoke(services, query, ct);
    }

    private static async Task Validate<TMessage>(IServiceProvider services, TMessage message, CancellationToken ct)
    {
        var failures = new List<FluentValidation.Results.ValidationFailure>();
        foreach (var validator in services.GetServices<IValidator<TMessage>>())
        {
            var result = await validator.ValidateAsync(message, ct);
            failures.AddRange(result.Errors);
        }

        if (failures.Count == 0)
            return;

        var errors = failures
            .GroupBy(f => f.PropertyName)
            .ToDictionary(g => g.Key, g => g.Select(f => f.ErrorMessage).Distinct().ToArray());
        throw new ValidationFailedException(errors);
    }

    private abstract class CommandInvoker<TResult>
    {
        public abstract Task<TResult> Invoke(IServiceProvider services, ICommand<TResult> command, CancellationToken ct);
    }

    private sealed class CommandInvoker<TCommand, TResult> : CommandInvoker<TResult>
        where TCommand : ICommand<TResult>
    {
        public override async Task<TResult> Invoke(IServiceProvider services, ICommand<TResult> command, CancellationToken ct)
        {
            var typed = (TCommand)command;
            await Validate(services, typed, ct);
            return await services.GetRequiredService<ICommandHandler<TCommand, TResult>>().Handle(typed, ct);
        }
    }

    private abstract class QueryInvoker<TResult>
    {
        public abstract Task<TResult> Invoke(IServiceProvider services, IQuery<TResult> query, CancellationToken ct);
    }

    private sealed class QueryInvoker<TQuery, TResult> : QueryInvoker<TResult>
        where TQuery : IQuery<TResult>
    {
        public override async Task<TResult> Invoke(IServiceProvider services, IQuery<TResult> query, CancellationToken ct)
        {
            var typed = (TQuery)query;
            await Validate(services, typed, ct);
            return await services.GetRequiredService<IQueryHandler<TQuery, TResult>>().Handle(typed, ct);
        }
    }
}
