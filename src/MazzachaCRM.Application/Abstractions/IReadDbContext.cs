using MazzachaCRM.Domain.Customers;
using MazzachaCRM.Domain.Expenses;
using MazzachaCRM.Domain.Ingredients;
using MazzachaCRM.Domain.Orders;
using MazzachaCRM.Domain.Production;
using MazzachaCRM.Domain.Products;
using MazzachaCRM.Domain.Settings;

namespace MazzachaCRM.Application.Abstractions;

/// <summary>
/// Сторона чтения: запросы без отслеживания изменений.
/// Обработчики запросов читают отсюда и собирают DTO, команды работают через репозитории.
/// </summary>
public interface IReadDbContext
{
    IQueryable<Ingredient> Ingredients { get; }
    IQueryable<StockMovement> StockMovements { get; }
    IQueryable<Product> Products { get; }
    IQueryable<ProductionBatch> Batches { get; }
    IQueryable<DailyExpense> Expenses { get; }
    IQueryable<Customer> Customers { get; }
    IQueryable<Order> Orders { get; }
    IQueryable<AppSettings> Settings { get; }
}

/// <summary>Где лежит файл базы: показывается в настройках, чтобы было понятно, что копировать.</summary>
public interface IDatabaseInfo
{
    string DatabasePath { get; }
}
