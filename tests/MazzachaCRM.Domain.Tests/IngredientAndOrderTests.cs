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

    private static Order NewOrder(decimal quantity = 1) =>
        Order.Create(1, Guid.NewGuid(), Today.AddDays(1), null, null, [new OrderItemSpec(Guid.NewGuid(), quantity, 100, null)]);

    [Fact]
    public void Balance_is_total_minus_payments()
    {
        var order = Order.Create(
            1, Guid.NewGuid(), Today, null, null,
            [new OrderItemSpec(Guid.NewGuid(), 2, 100, null), new OrderItemSpec(Guid.NewGuid(), 10, 3.5m, null)]);

        order.AddPayment(Today, 50, "Предоплата");
        order.AddPayment(Today.AddDays(1), 100, null);

        Assert.Equal(235m, order.Total);
        Assert.Equal(150m, order.Paid);
        Assert.Equal(85m, order.Balance);
    }

    [Fact]
    public void Order_goes_through_the_normal_flow_and_is_paid_once_balance_is_covered()
    {
        var order = NewOrder();

        order.ChangeStatus(OrderStatus.InProgress, Today);
        order.ChangeStatus(OrderStatus.Ready, Today);
        Assert.True(order.IsProduced);
        Assert.Equal(Today, order.ProducedDate);

        order.ChangeStatus(OrderStatus.Delivered, Today.AddDays(1));
        Assert.Equal(Today, order.ProducedDate);
        Assert.Equal(Today.AddDays(1), order.DeliveredDate);
        Assert.False(order.IsPaid);

        var payment = order.AddPayment(Today.AddDays(5), 100, null);
        Assert.True(order.IsPaid);

        order.RemovePayment(payment.Id);
        Assert.False(order.IsPaid);
    }

    [Fact]
    public void Delivered_order_can_only_be_returned_to_ready()
    {
        var order = NewOrder();
        order.ChangeStatus(OrderStatus.Delivered, Today);

        Assert.Throws<DomainException>(() => order.ChangeStatus(OrderStatus.Cancelled, Today));
        Assert.Throws<DomainException>(() => order.ChangeStatus(OrderStatus.New, Today));

        order.ChangeStatus(OrderStatus.Ready, Today);
        Assert.Null(order.DeliveredDate);
    }

    [Fact]
    public void Items_cannot_be_changed_after_the_order_is_produced()
    {
        var order = NewOrder();
        order.ChangeStatus(OrderStatus.Ready, Today);

        Assert.Throws<DomainException>(() => order.SetItems([new OrderItemSpec(Guid.NewGuid(), 2, 100, null)]));
    }

    [Fact]
    public void Shop_returns_reduce_the_amount_due()
    {
        var order = NewOrder(quantity: 10);
        order.ChangeStatus(OrderStatus.Delivered, Today);
        var item = order.Items.Single();

        order.RecordReturns(Today.AddDays(3), new Dictionary<Guid, decimal> { [item.Id] = 3 });

        Assert.Equal(7m, item.SoldQuantity);
        Assert.Equal(700m, order.Total);
        Assert.Equal(Today.AddDays(3), order.ReportedDate);
        Assert.Throws<DomainException>(() =>
            order.RecordReturns(Today.AddDays(3), new Dictionary<Guid, decimal> { [item.Id] = 11 }));
    }

    [Fact]
    public void Returns_require_a_produced_order_and_are_cleared_when_it_goes_back_to_work()
    {
        var order = NewOrder(quantity: 10);
        var item = order.Items.Single();
        Assert.Throws<DomainException>(() =>
            order.RecordReturns(Today, new Dictionary<Guid, decimal> { [item.Id] = 1 }));

        order.ChangeStatus(OrderStatus.Ready, Today);
        order.RecordReturns(Today, new Dictionary<Guid, decimal> { [item.Id] = 4 });
        order.ChangeStatus(OrderStatus.InProgress, Today);

        Assert.False(order.IsProduced);
        Assert.Null(order.ProducedDate);
        Assert.Null(order.ReportedDate);
        Assert.Equal(0m, item.ReturnedQuantity);
    }
}
