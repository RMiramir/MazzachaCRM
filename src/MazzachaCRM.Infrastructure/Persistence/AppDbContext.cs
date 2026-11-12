using MazzachaCRM.Application.Abstractions;
using MazzachaCRM.Domain.Customers;
using MazzachaCRM.Domain.Expenses;
using MazzachaCRM.Domain.Ingredients;
using MazzachaCRM.Domain.Orders;
using MazzachaCRM.Domain.Production;
using MazzachaCRM.Domain.Products;
using MazzachaCRM.Domain.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace MazzachaCRM.Infrastructure.Persistence;

/// <summary>
/// Один контекст играет три роли: хранилище для репозиториев, единица работы для команд
/// и сторона чтения без отслеживания для запросов.
/// </summary>
public sealed class AppDbContext(DbContextOptions<AppDbContext> options)
    : DbContext(options), IUnitOfWork, IReadDbContext
{
    public DbSet<Ingredient> Ingredients => Set<Ingredient>();
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductionBatch> Batches => Set<ProductionBatch>();
    public DbSet<DailyExpense> Expenses => Set<DailyExpense>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<AppSettings> Settings => Set<AppSettings>();

    IQueryable<Ingredient> IReadDbContext.Ingredients => Ingredients.AsNoTracking();
    IQueryable<StockMovement> IReadDbContext.StockMovements => StockMovements.AsNoTracking();
    IQueryable<Product> IReadDbContext.Products => Products.AsNoTracking();
    IQueryable<ProductionBatch> IReadDbContext.Batches => Batches.AsNoTracking();
    IQueryable<DailyExpense> IReadDbContext.Expenses => Expenses.AsNoTracking();
    IQueryable<Customer> IReadDbContext.Customers => Customers.AsNoTracking();
    IQueryable<Order> IReadDbContext.Orders => Orders.AsNoTracking();
    IQueryable<AppSettings> IReadDbContext.Settings => Settings.AsNoTracking();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        // Идентификаторы создаёт домен. Без этого EF принял бы новую дочернюю запись
        // с уже заполненным ключом за существующую и попытался бы её обновить.
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.FindPrimaryKey()?.Properties ?? [])
                property.ValueGenerated = ValueGenerated.Never;
        }
    }
}
