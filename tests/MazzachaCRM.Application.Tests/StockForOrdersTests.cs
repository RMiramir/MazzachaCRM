using MazzachaCRM.Application.Ingredients;
using MazzachaCRM.Application.Orders;
using MazzachaCRM.Application.Production;
using MazzachaCRM.Application.Products;
using MazzachaCRM.Application.Reports;
using MazzachaCRM.Application.Stock;
using MazzachaCRM.Domain.Common;
using MazzachaCRM.Domain.Ingredients;
using MazzachaCRM.Domain.Orders;
using MazzachaCRM.Domain.Products;

namespace MazzachaCRM.Application.Tests;

/// <summary>Готовые изделия в заказ: взять с витрины, вернуть обратно, не списывая сырьё дважды.</summary>
public class StockForOrdersTests : IAsyncLifetime
{
    private static readonly DateOnly Monday = new(2026, 11, 16);
    private static readonly DateOnly Tuesday = Monday.AddDays(1);

    private TestApp _app = null!;
    private Guid _flour;
    private Guid _trifle;

    public async Task InitializeAsync()
    {
        _app = await TestApp.CreateAsync();

        // Трайфл: 1000 г муки на 10 штук, то есть 100 г и 1 сомони за штуку; цена на витрине 20.
        _flour = await _app.Send(new CreateIngredientCommand(
            "Мука", IngredientKind.Ingredient, MeasureUnit.Gram, 10, 1000, null, 10000));
        _trifle = await _app.Send(new CreateProductCommand(
            "Трайфл", ProductType.Finished, 10, MeasureUnit.Piece, 0, 20, null,
            [new RecipeLineInput(_flour, null, 1000)]));
    }

    public async Task DisposeAsync() => await _app.DisposeAsync();

    private async Task<decimal> FlourStock() =>
        (await _app.Query(new GetIngredientsQuery())).Single(i => i.Id == _flour).StockQuantity;

    private Task<Guid> Order(DateOnly due, params OrderItemInput[] items) =>
        _app.Send(new CreateOrderCommand(null, "Мадина", null, due, null, 0, null, items));

    [Fact]
    public async Task Taking_part_of_the_showcase_into_an_order_leaves_the_rest_free()
    {
        var showcase = await _app.Send(new CreateBatchCommand(Monday, _trifle, 10, null, null));
        Assert.Equal(9000m, await FlourStock());

        var order = await Order(Monday, new OrderItemInput(_trifle, 5, 18, null, showcase));

        var day = await _app.Query(new GetDayQuery(Monday));
        Assert.Equal(2, day.Batches.Count);
        var free = day.Batches.Single(b => b.OrderId == null);
        var reserved = day.Batches.Single(b => b.OrderId != null);
        Assert.Equal(5m, free.Quantity);
        Assert.Equal(20m, free.UnitPrice);
        Assert.Equal(5m, reserved.Quantity);
        Assert.Equal(18m, reserved.UnitPrice);
        Assert.True(reserved.FromStock);
        Assert.Equal(1, reserved.OrderNumber);
        Assert.Equal(day.Batches[0].Id, free.Id); // свободные идут первыми, заказные сразу за ними

        // Сырьё повторно не списано, затраты дня те же.
        Assert.Equal(9000m, await FlourStock());
        Assert.Equal(10m, day.Batches.Sum(b => b.TotalCost));

        // Закреплённое нельзя продать с витрины.
        await Assert.ThrowsAsync<DomainException>(() =>
            _app.Send(new SetBatchSalesCommand(reserved.Id, Monday, [new SaleEntryInput(1, 20)])));

        var dto = await _app.Query(new GetOrderQuery(order));
        Assert.Equal(reserved.Id, dto.Items.Single().ReservedBatchId);
        Assert.Equal(5m, dto.Cost);
    }

    [Fact]
    public async Task Cancelling_the_order_returns_the_items_to_the_same_showcase_row()
    {
        var showcase = await _app.Send(new CreateBatchCommand(Monday, _trifle, 10, null, null));
        var order = await Order(Monday, new OrderItemInput(_trifle, 5, 18, null, showcase));

        await _app.Send(new ChangeOrderStatusCommand(order, OrderStatus.Cancelled, Monday));

        var row = Assert.Single((await _app.Query(new GetDayQuery(Monday))).Batches);
        Assert.Equal(showcase, row.Id);
        Assert.Equal(10m, row.Quantity);
        Assert.Null(row.OrderId);

        // Расход сырья снова числится за одной строкой: изменение количества пересчитывает его целиком.
        await _app.Send(new UpdateBatchCommand(showcase, 20, 20));
        Assert.Equal(8000m, await FlourStock());
    }

    [Fact]
    public async Task Taking_the_whole_row_moves_it_to_the_order_and_cancelling_restores_the_price()
    {
        var showcase = await _app.Send(new CreateBatchCommand(Monday, _trifle, 10, null, null));
        var order = await Order(Monday, new OrderItemInput(_trifle, 10, 15, null, showcase));

        var row = Assert.Single((await _app.Query(new GetDayQuery(Monday))).Batches);
        Assert.Equal(showcase, row.Id);
        Assert.Equal(1, row.OrderNumber);
        Assert.Equal(15m, row.UnitPrice);

        await _app.Send(new DeleteOrderCommand(order));

        row = Assert.Single((await _app.Query(new GetDayQuery(Monday))).Batches);
        Assert.Null(row.OrderId);
        Assert.Equal(20m, row.UnitPrice);
    }

    [Fact]
    public async Task Ready_bakes_only_what_was_not_taken_from_the_showcase()
    {
        // Вчера испекли 10, сегодня заказ: 4 из готового и ещё 10 нужно испечь.
        var showcase = await _app.Send(new CreateBatchCommand(Monday, _trifle, 10, null, null));
        var order = await Order(
            Tuesday,
            new OrderItemInput(_trifle, 4, 18, null, showcase),
            new OrderItemInput(_trifle, 10, 18, null));

        await _app.Send(new ChangeOrderStatusCommand(order, OrderStatus.Ready, Tuesday));

        // Испекли только вторую позицию: ещё килограмм муки.
        Assert.Equal(8000m, await FlourStock());

        var monday = await _app.Query(new GetDayQuery(Monday));
        Assert.Equal(10m, monday.Summary.ProductionCost); // затраты на готовое остались в дне выпечки
        Assert.Equal(0m, monday.Summary.Revenue);

        var tuesday = await _app.Query(new GetDayQuery(Tuesday));
        Assert.Equal(10m, tuesday.Summary.ProductionCost);     // только то, что пекли сегодня
        Assert.Equal(14 * 18m, tuesday.Summary.Revenue);       // выручка за весь заказ в день готовности
        Assert.Equal(0m, tuesday.Summary.Received);            // это долг, денег ещё не было
        Assert.Equal(14 * 18m, tuesday.Summary.DebtsTotal);
        var carried = Assert.Single(tuesday.CarryOver, c => c.OrderId != null);
        Assert.Equal(4m, carried.Sold);
        Assert.True(carried.OrderSaleLocked);

        var report = await _app.Query(new GetPeriodReportQuery(Monday, Tuesday));
        Assert.Equal(0m, report.Totals.Received);
        Assert.Equal(14 * 18m, report.Totals.Revenue);
    }

    [Fact]
    public async Task Returning_to_work_removes_only_what_was_baked_for_the_order()
    {
        var showcase = await _app.Send(new CreateBatchCommand(Monday, _trifle, 10, null, null));
        var order = await Order(
            Monday,
            new OrderItemInput(_trifle, 4, 18, null, showcase),
            new OrderItemInput(_trifle, 10, 18, null));
        await _app.Send(new ChangeOrderStatusCommand(order, OrderStatus.Ready, Monday));
        Assert.Equal(8000m, await FlourStock());

        await _app.Send(new ChangeOrderStatusCommand(order, OrderStatus.InProgress, Monday));

        Assert.Equal(9000m, await FlourStock());
        var day = await _app.Query(new GetDayQuery(Monday));
        Assert.Equal(2, day.Batches.Count);
        var reserved = Assert.Single(day.Batches, b => b.OrderId != null);
        Assert.Equal(4m, reserved.Quantity);
        Assert.Equal(0m, reserved.Sold);
        Assert.Equal(0m, day.Summary.Revenue);
    }

    [Fact]
    public async Task Editing_the_order_moves_items_between_the_showcase_and_the_order()
    {
        var showcase = await _app.Send(new CreateBatchCommand(Monday, _trifle, 10, null, null));
        var order = await Order(Monday, new OrderItemInput(_trifle, 5, 18, null, showcase));
        var dto = await _app.Query(new GetOrderQuery(order));
        var reservedId = dto.Items.Single().ReservedBatchId!.Value;

        // Для формы заказа доступно и свободное, и уже закреплённое за этим заказом.
        var stock = await _app.Query(new GetFreeStockQuery(order));
        Assert.Equal(5m, stock.Single(s => s.BatchId == showcase).Available);
        Assert.Equal(5m, stock.Single(s => s.BatchId == reservedId).Available);
        Assert.Single(await _app.Query(new GetFreeStockQuery(null)));

        // Уменьшили до трёх: две штуки вернулись на витрину в ту же строку.
        await _app.Send(new UpdateOrderCommand(
            order, dto.CustomerId, Monday, null, null, [new OrderItemInput(_trifle, 3, 18, null, reservedId)]));
        var day = await _app.Query(new GetDayQuery(Monday));
        Assert.Equal(7m, day.Batches.Single(b => b.OrderId == null).Quantity);
        Assert.Equal(3m, day.Batches.Single(b => b.OrderId != null).Quantity);
        Assert.Equal(2, day.Batches.Count);

        // Передумали брать из готового: всё вернулось, заказ испечётся отдельно.
        await _app.Send(new UpdateOrderCommand(
            order, dto.CustomerId, Monday, null, null, [new OrderItemInput(_trifle, 3, 18, null)]));
        var row = Assert.Single((await _app.Query(new GetDayQuery(Monday))).Batches);
        Assert.Equal(10m, row.Quantity);
        Assert.Equal(9000m, await FlourStock());

        // Больше, чем есть свободных, взять нельзя.
        await Assert.ThrowsAsync<DomainException>(() => _app.Send(new UpdateOrderCommand(
            order, dto.CustomerId, Monday, null, null, [new OrderItemInput(_trifle, 11, 18, null, showcase)])));
    }

    [Fact]
    public async Task Order_cannot_be_ready_before_the_day_its_stock_items_were_baked()
    {
        var showcase = await _app.Send(new CreateBatchCommand(Tuesday, _trifle, 10, null, null));
        var order = await Order(Tuesday, new OrderItemInput(_trifle, 5, 18, null, showcase));

        await Assert.ThrowsAsync<DomainException>(() =>
            _app.Send(new ChangeOrderStatusCommand(order, OrderStatus.Ready, Monday)));
    }

    [Fact]
    public async Task Shop_report_for_stock_items_is_booked_on_the_ready_day()
    {
        var showcase = await _app.Send(new CreateBatchCommand(Monday, _trifle, 10, null, null));
        var order = await Order(Tuesday, new OrderItemInput(_trifle, 10, 15, null, showcase));
        await _app.Send(new ChangeOrderStatusCommand(order, OrderStatus.Delivered, Tuesday));
        var itemId = (await _app.Query(new GetOrderQuery(order))).Items.Single().Id;

        await _app.Send(new SubmitShopReportCommand(order, Tuesday.AddDays(2), [new ShopReportItemInput(itemId, 7, 3)]));

        var tuesday = await _app.Query(new GetDayQuery(Tuesday));
        Assert.Equal(105m, tuesday.Summary.Revenue);
        Assert.Equal(3m, tuesday.Summary.WriteOffLoss); // три штуки по себестоимости 1
        Assert.Equal(3m, (await _app.Query(new GetOrderQuery(order))).Items.Single().ReturnedWrittenOff);
        Assert.Empty((await _app.Query(new GetDayQuery(Tuesday.AddDays(1)))).CarryOver);
    }

    [Fact]
    public async Task One_purchase_covers_several_products_and_creates_new_ones()
    {
        var count = await _app.Send(new RegisterPurchasesCommand(
            Monday,
            [
                new PurchaseLineInput(_flour, null, null, null, 5000, 60),                                  // мука подорожала: 12 за кг
                new PurchaseLineInput(null, "Сахар", IngredientKind.Ingredient, MeasureUnit.Gram, 2000, 26), // новый продукт
                new PurchaseLineInput(null, "Коробка", IngredientKind.Packaging, MeasureUnit.Piece, 10, 45),
                new PurchaseLineInput(null, "мука", null, MeasureUnit.Gram, 1000, 12, false),                // то же название — тот же продукт
            ],
            "Рынок"));

        Assert.Equal(4, count);
        var ingredients = await _app.Query(new GetIngredientsQuery());
        Assert.Equal(3, ingredients.Count);

        var flour = ingredients.Single(i => i.Name == "Мука");
        Assert.Equal(16000m, flour.StockQuantity);
        Assert.Equal(12m, flour.PackagePrice);

        var sugar = ingredients.Single(i => i.Name == "Сахар");
        Assert.Equal(2000m, sugar.StockQuantity);
        Assert.Equal(13m, sugar.PackagePrice);
        Assert.Equal(1000m, sugar.PackageSize);

        var box = ingredients.Single(i => i.Name == "Коробка");
        Assert.Equal(IngredientKind.Packaging, box.Kind);
        Assert.Equal(4.5m, box.PackagePrice);
        Assert.Equal(1m, box.PackageSize);

        var journal = await _app.Query(new GetStockMovementsQuery(null, Monday, Monday));
        Assert.Equal(143m, journal.PurchasesTotal);
    }

    [Fact]
    public async Task Resetting_stock_zeroes_everything_including_negative_balances()
    {
        var sugar = await _app.Send(new CreateIngredientCommand(
            "Сахар", IngredientKind.Ingredient, MeasureUnit.Gram, 12, 1000, null, null));
        await _app.Send(new WriteOffStockCommand(sugar, Monday, 300, null)); // сахар ушёл в минус
        var untouched = await _app.Send(new CreateIngredientCommand(
            "Соль", IngredientKind.Ingredient, MeasureUnit.Gram, 5, 1000, null, null));

        var changed = await _app.Send(new ResetStockCommand(Tuesday));

        Assert.Equal(2, changed); // мука и сахар; соль и так на нуле
        var ingredients = await _app.Query(new GetIngredientsQuery());
        Assert.All(ingredients, i => Assert.Equal(0m, i.StockQuantity));

        var journal = await _app.Query(new GetStockMovementsQuery(null, Tuesday, Tuesday));
        Assert.Equal(2, journal.Items.Count);
        Assert.All(journal.Items, m => Assert.Equal("Обнуление остатков", m.Note));
        Assert.DoesNotContain(journal.Items, m => m.IngredientId == untouched);
        Assert.Equal(300m, journal.Items.Single(m => m.IngredientId == sugar).Quantity);
    }
}
