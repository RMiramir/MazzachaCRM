using MazzachaCRM.Domain.Customers;
using MazzachaCRM.Domain.Expenses;
using MazzachaCRM.Domain.Ingredients;
using MazzachaCRM.Domain.Orders;
using MazzachaCRM.Domain.Production;
using MazzachaCRM.Domain.Products;
using MazzachaCRM.Domain.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MazzachaCRM.Infrastructure.Persistence.Configurations;

internal sealed class IngredientConfiguration : IEntityTypeConfiguration<Ingredient>
{
    public void Configure(EntityTypeBuilder<Ingredient> builder)
    {
        builder.ToTable("Ingredients");
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.HasIndex(x => x.Name).IsUnique();
    }
}

internal sealed class StockMovementConfiguration : IEntityTypeConfiguration<StockMovement>
{
    public void Configure(EntityTypeBuilder<StockMovement> builder)
    {
        builder.ToTable("StockMovements");
        builder.Property(x => x.Note).HasMaxLength(500);
        builder.HasOne<Ingredient>().WithMany().HasForeignKey(x => x.IngredientId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => new { x.IngredientId, x.Date });
        builder.HasIndex(x => x.BatchId);
    }
}

internal sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("Products");
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Note).HasMaxLength(1000);
        builder.HasIndex(x => x.Name).IsUnique();

        builder.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class RecipeLineConfiguration : IEntityTypeConfiguration<RecipeLine>
{
    public void Configure(EntityTypeBuilder<RecipeLine> builder)
    {
        builder.ToTable("RecipeLines");

        // Продукт или изделие нельзя удалить, пока они есть в чьём-то составе.
        builder.HasOne<Ingredient>().WithMany().HasForeignKey(x => x.IngredientId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Product>().WithMany().HasForeignKey(x => x.ComponentProductId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ProductionBatchConfiguration : IEntityTypeConfiguration<ProductionBatch>
{
    public void Configure(EntityTypeBuilder<ProductionBatch> builder)
    {
        builder.ToTable("Batches");
        builder.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Order>().WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.SetNull);

        builder.HasMany(x => x.Sales).WithOne().HasForeignKey(x => x.BatchId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.Sales).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(x => x.WriteOffs).WithOne().HasForeignKey(x => x.BatchId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.WriteOffs).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(x => x.ProductionDate);
        builder.HasIndex(x => x.ClosedOn);
    }
}

internal sealed class BatchSaleConfiguration : IEntityTypeConfiguration<BatchSale>
{
    public void Configure(EntityTypeBuilder<BatchSale> builder)
    {
        builder.ToTable("BatchSales");
        builder.HasIndex(x => x.Date);
    }
}

internal sealed class BatchWriteOffConfiguration : IEntityTypeConfiguration<BatchWriteOff>
{
    public void Configure(EntityTypeBuilder<BatchWriteOff> builder)
    {
        builder.ToTable("BatchWriteOffs");
        builder.Property(x => x.Reason).HasMaxLength(300);
        builder.HasIndex(x => x.Date);
    }
}

internal sealed class DailyExpenseConfiguration : IEntityTypeConfiguration<DailyExpense>
{
    public void Configure(EntityTypeBuilder<DailyExpense> builder)
    {
        builder.ToTable("Expenses");
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.HasIndex(x => x.Date);
    }
}

internal sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.ToTable("Customers");
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Phone).HasMaxLength(50);
        builder.Property(x => x.Note).HasMaxLength(1000);
    }
}

internal sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("Orders");
        builder.Property(x => x.Note).HasMaxLength(1000);
        builder.HasIndex(x => x.Number).IsUnique();
        builder.HasIndex(x => x.DueDate);
        builder.HasOne<Customer>().WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(x => x.Items).WithOne().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.Items).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class OrderItemConfiguration : IEntityTypeConfiguration<OrderItem>
{
    public void Configure(EntityTypeBuilder<OrderItem> builder)
    {
        builder.ToTable("OrderItems");
        builder.Property(x => x.Note).HasMaxLength(500);
        builder.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class AppSettingsConfiguration : IEntityTypeConfiguration<AppSettings>
{
    public void Configure(EntityTypeBuilder<AppSettings> builder)
    {
        builder.ToTable("Settings");
        builder.Property(x => x.Currency).HasMaxLength(20).IsRequired();
    }
}
