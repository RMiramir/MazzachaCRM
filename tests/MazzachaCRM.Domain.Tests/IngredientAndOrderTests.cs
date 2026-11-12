using MazzachaCRM.Domain.Common;
using MazzachaCRM.Domain.Ingredients;
using MazzachaCRM.Domain.Orders;

namespace MazzachaCRM.Domain.Tests;

public class IngredientTests
{
    private static readonly DateOnly Today = new(2026, 11, 12);

    [Fact]
    public void Unit_price_is_package_price_divided_by_package_size()
    {
        var flour = Ingredient.Create("Мука", IngredientKind.Ingredient, MeasureUnit.Gram, 12, 1000);

        Assert.Equal(0.012m, flour.UnitPrice);
    }

    [Fact]
    public void Purchase_increases_stock_and_updates_price_to_the_latest_one()
    {
        var flour = Ingredient.Create("Мука", IngredientKind.Ingredient, MeasureUnit.Gram, 8, 1000);

        // Купили 5 кг за 60: новая цена 12 за тот же килограмм.
        var movement = flour.RegisterPurchase(Today, 5000, 60, updatePrice: true, null);

        Assert.Equal(5000m, flour.StockQuantity);
        Assert.Equal(12m, flour.PackagePrice);
        Assert.Equal(1000m, flour.PackageSize);
        Assert.Equal(StockMovementType.Purchase, movement.Type);
        Assert.Equal(60m, movement.TotalCost);
    }

    [Fact]
    public void Purchase_can_leave_the_price_unchanged()
    {
        var flour = Ingredient.Create("Мука", IngredientKind.Ingredient, MeasureUnit.Gram, 8, 1000);

        flour.RegisterPurchase(Today, 5000, 60, updatePrice: false, null);

        Assert.Equal(8m, flour.PackagePrice);
    }

    [Fact]
    public void Stock_may_go_negative_and_reverting_a_movement_restores_it()
    {
        var flour = Ingredient.Create("Мука", IngredientKind.Ingredient, MeasureUnit.Gram, 8, 1000);

        var consumption = flour.ConsumeForBatch(Today, 300, Guid.NewGuid(), null);
        Assert.Equal(-300m, flour.StockQuantity);
        Assert.True(flour.IsOutOfStock);

        flour.RevertMovement(consumption);
        Assert.Equal(0m, flour.StockQuantity);
    }

    [Fact]
    public void Inventory_records_the_difference()
    {
        var flour = Ingredient.Create("Мука", IngredientKind.Ingredient, MeasureUnit.Gram, 8, 1000, minStock: 500);
        flour.RegisterPurchase(Today, 1000, 8, true, null);

        var adjustment = flour.AdjustStockTo(Today, 400, "Пересчитали");

        Assert.Equal(-600m, adjustment.Quantity);
        Assert.Equal(400m, flour.StockQuantity);
        Assert.True(flour.IsLowStock);
    }
}

public class OrderTests
{
    private static readonly DateOnly Today = new(2026, 11, 12);

    private static Order NewOrder() =>
        Order.Create(1, Guid.NewGuid(), Today.AddDays(1), null, 50, null, [new OrderItemSpec(Guid.NewGuid(), 1, 100, null)]);

    [Fact]
    public void Total_and_balance_account_for_prepayment()
    {
        var order = Order.Create(
            1, Guid.NewGuid(), Today, null, 50, null,
            [new OrderItemSpec(Guid.NewGuid(), 2, 100, null), new OrderItemSpec(Guid.NewGuid(), 10, 3.5m, null)]);

        Assert.Equal(235m, order.Total);
        Assert.Equal(185m, order.Balance);
    }

    [Fact]
    public void Order_goes_through_the_normal_flow()
    {
        var order = NewOrder();

        order.ChangeStatus(OrderStatus.InProgress, Today);
        order.ChangeStatus(OrderStatus.Ready, Today);
        Assert.True(order.IsProduced);
        Assert.Equal(Today, order.ProducedDate);

        order.ChangeStatus(OrderStatus.Completed, Today.AddDays(1));
        Assert.Equal(Today, order.ProducedDate);
        Assert.Equal(Today.AddDays(1), order.CompletedDate);
    }

    [Fact]
    public void Completed_order_can_only_be_returned_to_ready()
    {
        var order = NewOrder();
        order.ChangeStatus(OrderStatus.Completed, Today);

        Assert.Throws<DomainException>(() => order.ChangeStatus(OrderStatus.Cancelled, Today));
        Assert.Throws<DomainException>(() => order.ChangeStatus(OrderStatus.New, Today));

        order.ChangeStatus(OrderStatus.Ready, Today);
        Assert.Null(order.CompletedDate);
    }

    [Fact]
    public void Items_cannot_be_changed_after_the_order_is_produced()
    {
        var order = NewOrder();
        order.ChangeStatus(OrderStatus.Ready, Today);

        Assert.Throws<DomainException>(() => order.SetItems([new OrderItemSpec(Guid.NewGuid(), 2, 100, null)]));
    }

    [Fact]
    public void Returning_to_work_clears_production_dates()
    {
        var order = NewOrder();
        order.ChangeStatus(OrderStatus.Ready, Today);

        order.ChangeStatus(OrderStatus.InProgress, Today);

        Assert.False(order.IsProduced);
        Assert.Null(order.ProducedDate);
    }
}
