using MazzachaCRM.Application.Customers;
using MazzachaCRM.Application.Ingredients;
using MazzachaCRM.Application.Orders;
using MazzachaCRM.Application.Production;
using MazzachaCRM.Application.Products;
using MazzachaCRM.Domain.Common;
using MazzachaCRM.Domain.Ingredients;
using MazzachaCRM.Domain.Orders;
using MazzachaCRM.Domain.Products;

namespace MazzachaCRM.Application.Tests;

public class OrderFlowTests : IAsyncLifetime
{
    private static readonly DateOnly Friday = new(2026, 11, 13);
    private static readonly DateOnly Saturday = Friday.AddDays(1);

    private TestApp _app = null!;
    private Guid _flour;
    private Guid _cake;

    public async Task InitializeAsync()
    {
        _app = await TestApp.CreateAsync();

        // Торт: 2000 г муки по 10 за килограмм, себестоимость 20, цена 100.
        _flour = await _app.Send(new CreateIngredientCommand(
            "Мука", IngredientKind.Ingredient, MeasureUnit.Gram, 10, 1000, null, 10000));
        _cake = await _app.Send(new CreateProductCommand(
            "Торт", ProductType.Finished, 1, MeasureUnit.Piece, 0, 100, null,
            [new RecipeLineInput(_flour, null, 2000)]));
    }

    public async Task DisposeAsync() => await _app.DisposeAsync();

    private Task<Guid> CreateOrder(decimal quantity = 1, decimal prepayment = 50) =>
        _app.Send(new CreateOrderCommand(
            null, "Мадина", "+992 900 00 00 00", Saturday, new TimeOnly(15, 0), prepayment, "Без орехов",
            [new OrderItemInput(_cake, quantity, 100, null)]));

    private async Task<decimal> FlourStock() =>
        (await _app.Query(new GetIngredientsQuery())).Single(i => i.Id == _flour).StockQuantity;

    [Fact]
    public async Task New_order_creates_the_customer_and_shows_balance_and_expected_profit()
    {
        var id = await CreateOrder(quantity: 2);

        var order = await _app.Query(new GetOrderQuery(id));
        Assert.Equal(1, order.Number);
        Assert.Equal("Мадина", order.CustomerName);
        Assert.Equal(200m, order.Total);
        Assert.Equal(150m, order.Balance);
        Assert.Equal(40m, order.Cost);
        Assert.Equal(160m, order.Profit);
        Assert.Equal(OrderStatus.New, order.Status);

        var customer = Assert.Single(await _app.Query(new GetCustomersQuery("мадина")));
        Assert.Equal(1, customer.OrdersCount);
        Assert.Equal(200m, customer.OrdersTotal);

        // Новый заказ ещё не трогает склад и не попадает в затраты дня.
        Assert.Equal(10000m, await FlourStock());
        var saturday = await _app.Query(new GetDayQuery(Saturday));
        Assert.Single(saturday.Orders);
        Assert.Empty(saturday.Batches);
    }

    [Fact]
    public async Task Produced_order_costs_the_production_day_and_earns_on_the_delivery_day()
    {
        var id = await CreateOrder();

        await _app.Send(new ChangeOrderStatusCommand(id, OrderStatus.Ready, Friday));
        Assert.Equal(8000m, await FlourStock());

        var friday = await _app.Query(new GetDayQuery(Friday));
        var batch = Assert.Single(friday.Batches);
        Assert.Equal(1, batch.OrderNumber);
        Assert.Equal(20m, friday.Summary.TotalCosts);
        Assert.Equal(0m, friday.Summary.Revenue);

        await _app.Send(new ChangeOrderStatusCommand(id, OrderStatus.Completed, Saturday));

        var saturday = await _app.Query(new GetDayQuery(Saturday));
        Assert.Equal(100m, saturday.Summary.Revenue);
        Assert.Equal(80m, saturday.Summary.ProfitOnSold);
        Assert.Equal(0m, saturday.CarryOver.Single().Remaining);

        var order = await _app.Query(new GetOrderQuery(id));
        Assert.Equal(Friday, order.ProducedDate);
        Assert.Equal(Saturday, order.CompletedDate);
    }

    [Fact]
    public async Task Order_can_be_completed_in_one_step()
    {
        var id = await CreateOrder();

        await _app.Send(new ChangeOrderStatusCommand(id, OrderStatus.Completed, Saturday));

        var saturday = await _app.Query(new GetDayQuery(Saturday));
        Assert.Equal(20m, saturday.Summary.TotalCosts);
        Assert.Equal(100m, saturday.Summary.Revenue);
        Assert.Equal(80m, saturday.Summary.Profit);
    }

    [Fact]
    public async Task Returning_an_order_to_work_removes_batches_and_restores_stock()
    {
        var id = await CreateOrder();
        await _app.Send(new ChangeOrderStatusCommand(id, OrderStatus.Ready, Friday));

        await _app.Send(new ChangeOrderStatusCommand(id, OrderStatus.InProgress, Friday));

        Assert.Equal(10000m, await FlourStock());
        Assert.Empty((await _app.Query(new GetDayQuery(Friday))).Batches);
    }

    [Fact]
    public async Task Cancelling_a_produced_order_leaves_the_cake_as_ordinary_leftover()
    {
        var id = await CreateOrder();
        await _app.Send(new ChangeOrderStatusCommand(id, OrderStatus.Ready, Friday));

        await _app.Send(new ChangeOrderStatusCommand(id, OrderStatus.Cancelled, Friday));

        var friday = await _app.Query(new GetDayQuery(Friday));
        var batch = Assert.Single(friday.Batches);
        Assert.Null(batch.OrderId);
        Assert.Equal(1m, batch.Remaining);

        // Торт можно продать обычным покупателям.
        await _app.Send(new SetBatchSalesCommand(batch.Id, Saturday, [new SaleEntryInput(1, 90)]));
        Assert.Equal(90m, (await _app.Query(new GetDayQuery(Saturday))).Summary.Revenue);
    }

    [Fact]
    public async Task Order_batches_are_managed_only_through_the_order()
    {
        var id = await CreateOrder();
        await _app.Send(new ChangeOrderStatusCommand(id, OrderStatus.Ready, Friday));
        var batch = (await _app.Query(new GetDayQuery(Friday))).Batches.Single();

        await Assert.ThrowsAsync<DomainException>(() => _app.Send(new DeleteBatchCommand(batch.Id)));
        await Assert.ThrowsAsync<DomainException>(() =>
            _app.Send(new SetBatchSalesCommand(batch.Id, Friday, [new SaleEntryInput(1, 100)])));
        await Assert.ThrowsAsync<DomainException>(() => _app.Send(new DeleteOrderCommand(id)));
    }

    [Fact]
    public async Task Order_details_can_be_edited_but_items_are_locked_after_production()
    {
        var id = await CreateOrder();
        var order = await _app.Query(new GetOrderQuery(id));
        await _app.Send(new ChangeOrderStatusCommand(id, OrderStatus.Ready, Friday));

        // Предоплату и дату менять можно.
        await _app.Send(new UpdateOrderCommand(
            id, order.CustomerId, Saturday.AddDays(1), null, 100, "Перенесли",
            [new OrderItemInput(_cake, 1, 100, null)]));
        Assert.Equal(0m, (await _app.Query(new GetOrderQuery(id))).Balance);

        // Позиции уже изготовлены: менять нельзя.
        await Assert.ThrowsAsync<DomainException>(() => _app.Send(new UpdateOrderCommand(
            id, order.CustomerId, Saturday, null, 100, null,
            [new OrderItemInput(_cake, 2, 100, null)])));
    }

    [Fact]
    public async Task Customer_with_orders_cannot_be_deleted()
    {
        var id = await CreateOrder();
        var order = await _app.Query(new GetOrderQuery(id));

        await Assert.ThrowsAsync<DomainException>(() => _app.Send(new DeleteCustomerCommand(order.CustomerId)));

        await _app.Send(new DeleteOrderCommand(id));
        await _app.Send(new DeleteCustomerCommand(order.CustomerId));
        Assert.Empty(await _app.Query(new GetCustomersQuery(null)));
    }

    [Fact]
    public async Task Order_numbers_are_sequential()
    {
        await CreateOrder();
        var second = await CreateOrder();

        Assert.Equal(2, (await _app.Query(new GetOrderQuery(second))).Number);
    }
}
