namespace MazzachaCRM.Application.Abstractions;

/// <summary>Команда: меняет состояние. После обработчика диспетчер сохраняет изменения.</summary>
public interface ICommand<TResult>;

/// <summary>Запрос: только читает данные и возвращает DTO.</summary>
public interface IQuery<TResult>;

public interface ICommandHandler<in TCommand, TResult>
    where TCommand : ICommand<TResult>
{
    Task<TResult> Handle(TCommand command, CancellationToken ct);
}

public interface IQueryHandler<in TQuery, TResult>
    where TQuery : IQuery<TResult>
{
    Task<TResult> Handle(TQuery query, CancellationToken ct);
}

/// <summary>Единая точка входа из API в слой приложения.</summary>
public interface IDispatcher
{
    Task<TResult> Send<TResult>(ICommand<TResult> command, CancellationToken ct = default);
    Task<TResult> Query<TResult>(IQuery<TResult> query, CancellationToken ct = default);
}

/// <summary>Результат команды, которой нечего возвращать.</summary>
public readonly record struct Unit
{
    public static readonly Unit Value = default;
}

public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken ct);
}
