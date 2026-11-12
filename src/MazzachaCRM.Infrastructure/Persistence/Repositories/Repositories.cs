using MazzachaCRM.Domain.Customers;
using MazzachaCRM.Domain.Expenses;
using MazzachaCRM.Domain.Ingredients;
using MazzachaCRM.Domain.Orders;
using MazzachaCRM.Domain.Production;
using MazzachaCRM.Domain.Products;
using MazzachaCRM.Domain.Settings;
using Microsoft.EntityFrameworkCore;

namespace MazzachaCRM.Infrastructure.Persistence.Repositories;

internal static class NameComparison
{
    /// <summary>Сравнение без учёта регистра делается в памяти: SQLite не умеет этого для кириллицы.</summary>
    public static bool ContainsName(this IEnumerable<string> names, string name) =>
        names.Any(n => string.Equals(n.Trim(), name.Trim(), StringComparison.CurrentCultureIgnoreCase));
}

internal sealed class IngredientRepository(AppDbContext db) : IIngredientRepository
{
    public Task<Ingredient?> GetByIdAsync(Guid id, CancellationToken ct) =>
        db.Ingredients.FirstOrDefaultAsync(x => x.Id == id, ct);

    public Task<List<Ingredient>> ListAsync(CancellationToken ct) => db.Ingredients.ToListAsync(ct);

    public async Task<bool> NameExistsAsync(string name, Guid? exceptId, CancellationToken ct)
    {
        var names = await db.Ingredients.Where(x => x.Id != exceptId).Select(x => x.Name).ToListAsync(ct);
        return names.ContainsName(name);
    }

    public void Add(Ingredient ingredient) => db.Ingredients.Add(ingredient);

    public void Remove(Ingredient ingredient) => db.Ingredients.Remove(ingredient);
}

internal sealed class StockMovementRepository(AppDbContext db) : IStockMovementRepository
{
    public Task<StockMovement?> GetByIdAsync(Guid id, CancellationToken ct) =>
        db.StockMovements.FirstOrDefaultAsync(x => x.Id == id, ct);

    public Task<List<StockMovement>> ListByBatchAsync(Guid batchId, CancellationToken ct) =>
        db.StockMovements.Where(x => x.BatchId == batchId).ToListAsync(ct);

    public Task<bool> AnyForIngredientAsync(Guid ingredientId, CancellationToken ct) =>
        db.StockMovements.AnyAsync(x => x.IngredientId == ingredientId, ct);

    public void Add(StockMovement movement) => db.StockMovements.Add(movement);

    public void Remove(StockMovement movement) => db.StockMovements.Remove(movement);
}

internal sealed class ProductRepository(AppDbContext db) : IProductRepository
{
    public Task<Product?> GetByIdAsync(Guid id, CancellationToken ct) =>
        db.Products.Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id, ct);

    public Task<List<Product>> ListAsync(CancellationToken ct) =>
        db.Products.Include(x => x.Lines).ToListAsync(ct);

    public async Task<bool> NameExistsAsync(string name, Guid? exceptId, CancellationToken ct)
    {
        var names = await db.Products.Where(x => x.Id != exceptId).Select(x => x.Name).ToListAsync(ct);
        return names.ContainsName(name);
    }

    public Task<List<string>> ListNamesUsingIngredientAsync(Guid ingredientId, CancellationToken ct) =>
        db.Products
            .Where(p => p.Lines.Any(l => l.IngredientId == ingredientId))
            .OrderBy(p => p.Name)
            .Select(p => p.Name)
            .ToListAsync(ct);

    public Task<List<string>> ListNamesUsingProductAsync(Guid componentProductId, CancellationToken ct) =>
        db.Products
            .Where(p => p.Lines.Any(l => l.ComponentProductId == componentProductId))
            .OrderBy(p => p.Name)
            .Select(p => p.Name)
            .ToListAsync(ct);

    public void Add(Product product) => db.Products.Add(product);

    public void Remove(Product product) => db.Products.Remove(product);
}

internal sealed class ProductionBatchRepository(AppDbContext db) : IProductionBatchRepository
{
    public Task<ProductionBatch?> GetByIdAsync(Guid id, CancellationToken ct) =>
        db.Batches.Include(x => x.Sales).Include(x => x.WriteOffs).FirstOrDefaultAsync(x => x.Id == id, ct);

    public Task<List<ProductionBatch>> ListByOrderAsync(Guid orderId, CancellationToken ct) =>
        db.Batches.Include(x => x.Sales).Include(x => x.WriteOffs).Where(x => x.OrderId == orderId).ToListAsync(ct);

    public Task<bool> AnyForProductAsync(Guid productId, CancellationToken ct) =>
        db.Batches.AnyAsync(x => x.ProductId == productId, ct);

    public void Add(ProductionBatch batch) => db.Batches.Add(batch);

    public void Remove(ProductionBatch batch) => db.Batches.Remove(batch);
}

internal sealed class DailyExpenseRepository(AppDbContext db) : IDailyExpenseRepository
{
    public Task<DailyExpense?> GetByIdAsync(Guid id, CancellationToken ct) =>
        db.Expenses.FirstOrDefaultAsync(x => x.Id == id, ct);

    public void Add(DailyExpense expense) => db.Expenses.Add(expense);

    public void Remove(DailyExpense expense) => db.Expenses.Remove(expense);
}

internal sealed class CustomerRepository(AppDbContext db) : ICustomerRepository
{
    public Task<Customer?> GetByIdAsync(Guid id, CancellationToken ct) =>
        db.Customers.FirstOrDefaultAsync(x => x.Id == id, ct);

    public void Add(Customer customer) => db.Customers.Add(customer);

    public void Remove(Customer customer) => db.Customers.Remove(customer);
}

internal sealed class OrderRepository(AppDbContext db) : IOrderRepository
{
    public Task<Order?> GetByIdAsync(Guid id, CancellationToken ct) =>
        db.Orders.Include(x => x.Items).FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<int> NextNumberAsync(CancellationToken ct) =>
        (await db.Orders.MaxAsync(x => (int?)x.Number, ct) ?? 0) + 1;

    public Task<bool> AnyForCustomerAsync(Guid customerId, CancellationToken ct) =>
        db.Orders.AnyAsync(x => x.CustomerId == customerId, ct);

    public Task<bool> AnyForProductAsync(Guid productId, CancellationToken ct) =>
        db.Orders.AnyAsync(x => x.Items.Any(i => i.ProductId == productId), ct);

    public void Add(Order order) => db.Orders.Add(order);

    public void Remove(Order order) => db.Orders.Remove(order);
}

internal sealed class SettingsRepository(AppDbContext db) : ISettingsRepository
{
    public async Task<AppSettings> GetAsync(CancellationToken ct)
    {
        var settings = db.Settings.Local.FirstOrDefault() ?? await db.Settings.OrderBy(s => s.Id).FirstOrDefaultAsync(ct);
        if (settings is null)
        {
            settings = AppSettings.CreateDefault();
            db.Settings.Add(settings);
        }

        return settings;
    }
}
