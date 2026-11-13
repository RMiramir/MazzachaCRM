using MazzachaCRM.Application.Customers;
using MazzachaCRM.Application.Ingredients;
using MazzachaCRM.Application.Orders;
using MazzachaCRM.Application.Production;
using MazzachaCRM.Application.Products;
using MazzachaCRM.Application.Reports;
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
    public async Task Ready_order_books_cost_and_revenue_on_the_ready_day_and_money_when_paid()
    {
        var id = await CreateOrder(prepayment: 30);

        await _app.Send(new ChangeOrderStatusCommand(id, OrderStatus.Ready, Friday));
        Assert.Equal(8000m, await FlourStock());

        var friday = await _app.Query(new GetDayQuery(Friday));
        var batch = Assert.Single(friday.Batches);
        Assert.Equal(1, batch.OrderNumber);
        Assert.Equal(20m, friday.Summary.TotalCosts);
        Assert.Equal(100m, friday.Summary.Revenue);
        Assert.Equal(80m, friday.Summary.Profit);
        Assert.Equal(70m, friday.Summary.DebtsTotal);
        Assert.Contains(friday.Orders, o => o.Id == id);

        // Выдача ничего не меняет в цифрах, доплата приходит деньгами в субботу.
        await _app.Send(new ChangeOrderStatusCommand(id, OrderStatus.Delivered, Saturday));
        await _app.Send(new AddOrderPaymentCommand(id, Saturday, 70, null));

        var saturday = await _app.Query(new GetDayQuery(Saturday));
        Assert.Equal(0m, saturday.Summary.Revenue);
        Assert.Equal(70m, saturday.Summary.Received);
        Assert.Equal(0m, saturday.Summary.DebtsTotal);
        Assert.Empty(saturday.CarryOver);

        var order = await _app.Query(new GetOrderQuery(id));
        Assert.True(order.IsPaid);
        Assert.Equal(Friday, order.ProducedDate);
        Assert.Equal(Saturday, order.DeliveredDate);
        Assert.Equal(2, order.Payments.Count);
        Assert.Equal(70m, (await _app.Query(new GetPeriodReportQuery(Saturday, Saturday))).Totals.Received);
    }

    [Fact]
    public async Task Order_can_be_delivered_in_one_step()
    {
        var id = await CreateOrder();

        await _app.Send(new ChangeOrderStatusCommand(id, OrderStatus.Delivered, Saturday));

        var saturday = await _app.Query(new GetDayQuery(Saturday));
        Assert.Equal(20m, saturday.Summary.TotalCosts);
        Assert.Equal(100m, saturday.Summary.Revenue);
        Assert.Equal(80m, saturday.Summary.Profit);
    }

    [Fact]
    public async Task Shop_report_reduces_revenue_of_the_shipping_day_and_returns_become_leftovers()
    {
        var shop = await _app.Send(new CreateCustomerCommand("Магазин", null, null, true));
        var id = await _app.Send(new CreateOrderCommand(
            shop, null, null, Friday, null, 0, null, [new OrderItemInput(_cake, 10, 50, null)]));
        await _app.Send(new ChangeOrderStatusCommand(id, OrderStatus.Delivered, Friday));

        var friday = await _app.Query(new GetDayQuery(Friday));
        Assert.Equal(500m, friday.Summary.Revenue);
        Assert.True(friday.Batches.Single().OrderIsConsignment);
        Assert.Equal(500m, friday.Summary.DebtsTotal);

        // Через три дня магазин сообщил: продано 7, три вернули.
        var itemId = (await _app.Query(new GetOrderQuery(id))).Items.Single().Id;
        var reportDay = Friday.AddDays(3);
        await _app.Send(new SubmitShopReportCommand(id, reportDay, [new ShopReportItemInput(itemId, 7)]));

        friday = await _app.Query(new GetDayQuery(Friday));
        Assert.Equal(350m, friday.Summary.Revenue);
        Assert.Equal(3m, friday.Batches.Single().OrderReturned);
        Assert.Equal(3m, friday.Batches.Single().Remaining);

        // Возврат виден в остатках и продаётся с витрины как обычно.
        var report = await _app.Query(new GetDayQuery(reportDay));
        var leftover = Assert.Single(report.CarryOver);
        Assert.Equal(3m, leftover.RemainingAtStart);
        await _app.Send(new SetBatchSalesCommand(leftover.Id, reportDay, [new SaleEntryInput(2, 40)]));
        report = await _app.Query(new GetDayQuery(reportDay));
        Assert.Equal(80m, report.Summary.Revenue);
        Assert.Equal(80m, report.Summary.Received);

        // Магазин должен только за проданное.
        await _app.Send(new AddOrderPaymentCommand(id, reportDay, 200, "Магазин, частично"));
        var order = await _app.Query(new GetOrderQuery(id));
        Assert.Equal(350m, order.Total);
        Assert.Equal(150m, order.Balance);
        Assert.Equal(150m, Assert.Single(await _app.Query(new GetCustomersQuery("Магазин"))).Debt);
        Assert.Single(await _app.Query(new GetOrdersQuery(OrderScope.Unpaid, null, null, null)));
    }

    [Fact]
    public async Task Shop_returns_can_be_written_off_at_once_and_then_do_not_carry_over()
    {
        var shop = await _app.Send(new CreateCustomerCommand("Магазин", null, null, true));
        var id = await _app.Send(new CreateOrderCommand(
            shop, null, null, Friday, null, 0, null, [new OrderItemInput(_cake, 10, 50, null)]));
        await _app.Send(new ChangeOrderStatusCommand(id, OrderStatus.Delivered, Friday));
        var itemId = (await _app.Query(new GetOrderQuery(id))).Items.Single().Id;

        // Продано 7, из трёх возвращённых два списали сразу, один переходит в остатки.
        await _app.Send(new SubmitShopReportCommand(id, Saturday, [new ShopReportItemInput(itemId, 7, 2)]));

        var friday = await _app.Query(new GetDayQuery(Friday));
        Assert.Equal(350m, friday.Summary.Revenue);
        Assert.Equal(2m, friday.Batches.Single().WrittenOff);
        Assert.Equal(1m, friday.Batches.Single().Remaining);
        Assert.Equal(170m, friday.Summary.ActualProfit);
        Assert.Equal(200m, friday.Summary.PotentialProfit); // плюс один торт в остатке: 50 − 20
        Assert.Equal(1m, (await _app.Query(new GetDayQuery(Saturday))).CarryOver.Single().RemainingAtStart);
        Assert.Equal(2m, (await _app.Query(new GetOrderQuery(id))).Items.Single().ReturnedWrittenOff);

        // Списали весь возврат: в остатках ничего не остаётся.
        await _app.Send(new SubmitShopReportCommand(id, Saturday, [new ShopReportItemInput(itemId, 7, 3)]));
        Assert.Empty((await _app.Query(new GetDayQuery(Saturday))).CarryOver);

        // Списать больше, чем вернули, нельзя.
        await Assert.ThrowsAsync<DomainException>(() =>
            _app.Send(new SubmitShopReportCommand(id, Saturday, [new ShopReportItemInput(itemId, 7, 4)])));
    }

    [Fact]
    public async Task Sales_of_an_order_batch_on_its_ready_day_are_managed_by_the_order()
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
        Assert.Equal(0m, friday.Summary.Revenue);

        await _app.Send(new SetBatchSalesCommand(batch.Id, Saturday, [new SaleEntryInput(1, 90)]));
        Assert.Equal(90m, (await _app.Query(new GetDayQuery(Saturday))).Summary.Revenue);
    }

    [Fact]
    public async Task Order_details_can_be_edited_but_items_are_locked_after_production()
    {
        var id = await CreateOrder();
        var order = await _app.Query(new GetOrderQuery(id));
        await _app.Send(new ChangeOrderStatusCommand(id, OrderStatus.Ready, Friday));

        await _app.Send(new UpdateOrderCommand(
            id, order.CustomerId, Saturday.AddDays(1), null, "Перенесли",
            [new OrderItemInput(_cake, 1, 100, null)]));
        Assert.Equal(Saturday.AddDays(1), (await _app.Query(new GetOrderQuery(id))).DueDate);

        await Assert.ThrowsAsync<DomainException>(() => _app.Send(new UpdateOrderCommand(
            id, order.CustomerId, Saturday, null, null,
            [new OrderItemInput(_cake, 2, 100, null)])));
    }

    [Fact]
    public async Task Repeat_creates_a_new_order_with_the_same_items()
    {
        var id = await CreateOrder(quantity: 3);

        var copy = await _app.Send(new RepeatOrderCommand(id, Saturday.AddDays(7)));

        var order = await _app.Query(new GetOrderQuery(copy));
        Assert.Equal(2, order.Number);
        Assert.Equal(Saturday.AddDays(7), order.DueDate);
        Assert.Equal(300m, order.Total);
        Assert.Equal(0m, order.Paid);
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
