using System.Data.Common;
using MazzachaCRM.Application.Abstractions;
using MazzachaCRM.Domain.Customers;
using MazzachaCRM.Domain.Expenses;
using MazzachaCRM.Domain.Ingredients;
using MazzachaCRM.Domain.Orders;
using MazzachaCRM.Domain.Production;
using MazzachaCRM.Domain.Products;
using MazzachaCRM.Domain.Settings;
using MazzachaCRM.Infrastructure.Persistence;
using MazzachaCRM.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace MazzachaCRM.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Рабочий режим: база в файле по указанному пути.</summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, string databasePath)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(databasePath));
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        services.AddDbContext<AppDbContext>(options => options.UseSqlite($"Data Source={databasePath}", UseSplitQueries));
        return services.AddPersistence(Path.GetFullPath(databasePath));
    }

    /// <summary>Для тестов: база на уже открытом соединении (например, в памяти).</summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, DbConnection connection)
    {
        services.AddDbContext<AppDbContext>(options => options.UseSqlite(connection, UseSplitQueries));
        return services.AddPersistence(connection.DataSource);
    }

    private static IServiceCollection AddPersistence(this IServiceCollection services, string databasePath)
    {
        services.AddSingleton<IDatabaseInfo>(new DatabaseInfo(databasePath));
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<AppDbContext>());
        services.AddScoped<IReadDbContext>(sp => sp.GetRequiredService<AppDbContext>());

        services.AddScoped<IIngredientRepository, IngredientRepository>();
        services.AddScoped<IStockMovementRepository, StockMovementRepository>();
        services.AddScoped<IProductRepository, ProductRepository>();
        services.AddScoped<IProductionBatchRepository, ProductionBatchRepository>();
        services.AddScoped<IDailyExpenseRepository, DailyExpenseRepository>();
        services.AddScoped<ICustomerRepository, CustomerRepository>();
        services.AddScoped<IOrderRepository, OrderRepository>();
        services.AddScoped<ISettingsRepository, SettingsRepository>();

        services.AddScoped<DatabaseInitializer>();
        services.AddScoped<DemoDataSeeder>();
        return services;
    }

    /// <summary>
    /// Партия читается сразу с продажами и списаниями. Отдельный запрос на каждую коллекцию
    /// не даёт строкам перемножаться в одном большом соединении.
    /// </summary>
    private static void UseSplitQueries(SqliteDbContextOptionsBuilder sqlite) =>
        sqlite.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery);

    private sealed record DatabaseInfo(string DatabasePath) : IDatabaseInfo;
}
