using System.Globalization;
using FluentValidation;
using MazzachaCRM.Application.Abstractions;
using MazzachaCRM.Application.Common;
using MazzachaCRM.Application.Production;
using Microsoft.Extensions.DependencyInjection;

namespace MazzachaCRM.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        var assembly = typeof(DependencyInjection).Assembly;

        services.AddScoped<IDispatcher, Dispatcher>();
        services.AddScoped<CostingContextLoader>();
        services.AddScoped<BatchProductionService>();

        // Сообщения валидации на русском.
        ValidatorOptions.Global.LanguageManager.Culture = new CultureInfo("ru");
        services.AddValidatorsFromAssembly(assembly, includeInternalTypes: true);

        // Все обработчики команд и запросов регистрируются по интерфейсу.
        foreach (var type in assembly.GetTypes().Where(t => t is { IsClass: true, IsAbstract: false }))
        {
            foreach (var contract in type.GetInterfaces().Where(IsHandlerContract))
                services.AddScoped(contract, type);
        }

        return services;
    }

    private static bool IsHandlerContract(Type type) =>
        type.IsGenericType &&
        (type.GetGenericTypeDefinition() == typeof(ICommandHandler<,>) ||
         type.GetGenericTypeDefinition() == typeof(IQueryHandler<,>));
}
