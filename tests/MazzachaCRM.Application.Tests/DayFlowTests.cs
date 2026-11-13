using MazzachaCRM.Application.Common;
using MazzachaCRM.Application.Expenses;
using MazzachaCRM.Application.Ingredients;
using MazzachaCRM.Application.Production;
using MazzachaCRM.Application.Products;
using MazzachaCRM.Application.Reports;
using MazzachaCRM.Application.Settings;
using MazzachaCRM.Application.Stock;
using MazzachaCRM.Domain.Common;
using MazzachaCRM.Domain.Ingredients;
using MazzachaCRM.Domain.Products;

namespace MazzachaCRM.Application.Tests;

public class DayFlowTests : IAsyncLifetime
{
    private static readonly DateOnly Monday = new(2026, 11, 9);
    private static readonly DateOnly Tuesday = Monday.AddDays(1);
    private static readonly DateOnly Wednesday = Monday.AddDays(2);

    private TestApp _app = null!;
    private Guid _flour;
    private Guid _muffin;

    public async Task InitializeAsync()
    {
        _app = await TestApp.CreateAsync();

        // Мука по 10 за килограмм; маффин: 1000 г муки на 10 штук, то есть 1 за штуку.
        _flour = await _app.Send(new CreateIngredientCommand(
            "Мука", IngredientKind.Ingredient, MeasureUnit.Gram, 10, 1000, null, 5000));
        _muffin = await _app.Send(new CreateProductCommand(
            "Маффин", ProductType.Finished, 10, MeasureUnit.Piece, 0, 3, null,
            [new RecipeLineInput(_flour, null, 1000)]));
    }

    public async Task DisposeAsync() => await _app.DisposeAsync();

    private async Task<decimal> FlourStock() =>
        (await _app.Query(new GetIngredientsQuery())).Single(i => i.Id == _flour).StockQuantity;

    [Fact]
    public async Task Unsold_leftovers_appear_the_next_day_and_their_sale_counts_there()
    {
        var batch = await _app.Send(new CreateBatchCommand(Monday, _muffin, 20, null, 12));

        var monday = await _app.Query(new GetDayQuery(Monday));
        Assert.Equal(20m, monday.Summary.TotalCosts);
        Assert.Equal(36m, monday.Summary.Revenue);
        Assert.Equal(8m, monday.Batches.Single().Remaining);

        // Во вторник остаток виден как перенесённый; продаём 5 со скидкой и списываем 3.
        var tuesdayBefore = await _app.Query(new GetDayQuery(Tuesday));
        Assert.Empty(tuesdayBefore.Batches);
        Assert.Equal(8m, tuesdayBefore.CarryOver.Single().RemainingAtStart);

        await _app.Send(new SetBatchSalesCommand(batch, Tuesday, [new SaleEntryInput(5, 2)]));
        await _app.Send(new SetBatchWriteOffCommand(batch, Tuesday, 3, "Зачерствели"));

        var tuesday = await _app.Query(new GetDayQuery(Tuesday));
        Assert.Equal(0m, tuesday.Summary.TotalCosts);   // во вторник ничего не пекли
        Assert.Equal(10m, tuesday.Summary.Revenue);
        Assert.Equal(10m, tuesday.Summary.Profit);
        Assert.Equal(5m, tuesday.Summary.ProfitOnSold); // 5 штук: продали по 2, себестоимость 1
        Assert.Equal(3m, tuesday.Summary.WriteOffLoss);
        Assert.Equal(0m, tuesday.CarryOver.Single().Remaining);

        // В понедельнике ничего не изменилось, но видно, что остаток ушёл позже.
        var mondayAfter = await _app.Query(new GetDayQuery(Monday));
        Assert.Equal(36m, mondayAfter.Summary.Revenue);
        Assert.Equal(5m, mondayAfter.Batches.Single().SoldLater);
        Assert.Equal(3m, mondayAfter.Batches.Single().WrittenOffLater);

        // В среду партия закрыта и больше не показывается.
        var wednesday = await _app.Query(new GetDayQuery(Wednesday));
        Assert.Empty(wednesday.CarryOver);
    }

    [Fact]
    public async Task Daily_expenses_reduce_profit_of_the_day()
    {
        await _app.Send(new CreateBatchCommand(Monday, _muffin, 10, null, 10));
        var taxi = await _app.Send(new AddExpenseCommand(Monday, "Такси", 7));

        var day = await _app.Query(new GetDayQuery(Monday));
        Assert.Equal(10m, day.Summary.ProductionCost);
        Assert.Equal(7m, day.Summary.Expenses);
        Assert.Equal(17m, day.Summary.TotalCosts);
        Assert.Equal(13m, day.Summary.Profit);

        await _app.Send(new DeleteExpenseCommand(taxi));
        Assert.Equal(20m, (await _app.Query(new GetDayQuery(Monday))).Summary.Profit);
    }

    [Fact]
    public async Task Production_consumes_stock_and_edits_keep_it_in_sync()
    {
        var batch = await _app.Send(new CreateBatchCommand(Monday, _muffin, 20, null, null));
        Assert.Equal(3000m, await FlourStock());

        await _app.Send(new UpdateBatchCommand(batch, 30, 3));
        Assert.Equal(2000m, await FlourStock());

        await _app.Send(new DeleteBatchCommand(batch));
        Assert.Equal(5000m, await FlourStock());

        var movements = await _app.Query(new GetStockMovementsQuery(_flour, null, null));
        Assert.DoesNotContain(movements.Items, m => m.Type == StockMovementType.Production);
    }

    [Fact]
    public async Task Recorded_days_keep_their_cost_when_the_ingredient_price_changes()
    {
        var batch = await _app.Send(new CreateBatchCommand(Monday, _muffin, 10, null, null));

        // Мука подорожала вдвое: новая закупка обновляет текущую цену.
        await _app.Send(new RegisterPurchaseCommand(_flour, Tuesday, 1000, 20, true, null));

        var products = await _app.Query(new GetProductsQuery(false));
        Assert.Equal(2m, products.Single().UnitCost!.Total);

        var monday = await _app.Query(new GetDayQuery(Monday));
        Assert.Equal(1m, monday.Batches.Single().UnitCost);

        // Пересчёт по текущим ценам делается только по явной команде.
        await _app.Send(new RecalculateBatchCostCommand(batch));
        Assert.Equal(2m, (await _app.Query(new GetDayQuery(Monday))).Batches.Single().UnitCost);
    }

    [Fact]
    public async Task Labor_is_part_of_the_cost_when_an_hourly_rate_is_set()
    {
        await _app.Send(new UpdateSettingsCommand(60, "сомони"));
        var cake = await _app.Send(new CreateProductCommand(
            "Торт", ProductType.Finished, 1, MeasureUnit.Piece, 30, 100, null,
            [new RecipeLineInput(_flour, null, 500)]));

        await _app.Send(new CreateBatchCommand(Monday, cake, 2, null, null));

        var day = await _app.Query(new GetDayQuery(Monday));
        Assert.Equal(35m, day.Batches.Single().UnitCost); // 5 за муку и 30 за полчаса работы
        Assert.Equal(70m, day.Summary.ProductionCost);
        Assert.Equal(60m, day.Summary.LaborCost);
    }

    [Fact]
    public async Task Semi_finished_products_cannot_be_put_into_the_day_table()
    {
        var cream = await _app.Send(new CreateProductCommand(
            "Крем", ProductType.SemiFinished, 500, MeasureUnit.Gram, 0, 0, null,
            [new RecipeLineInput(_flour, null, 100)]));

        await Assert.ThrowsAsync<DomainException>(() => _app.Send(new CreateBatchCommand(Monday, cream, 1, null, null)));
    }

    [Fact]
    public async Task Ingredient_used_in_a_recipe_cannot_be_deleted()
    {
        var error = await Assert.ThrowsAsync<DomainException>(() => _app.Send(new DeleteIngredientCommand(_flour)));
        Assert.Contains("Маффин", error.Message);
    }

    [Fact]
    public async Task Product_with_history_is_archived_instead_of_deleted()
    {
        await _app.Send(new CreateBatchCommand(Monday, _muffin, 10, null, null));

        await Assert.ThrowsAsync<DomainException>(() => _app.Send(new DeleteProductCommand(_muffin)));

        await _app.Send(new SetProductArchivedCommand(_muffin, true));
        Assert.Empty(await _app.Query(new GetProductsQuery(false)));
        Assert.Single(await _app.Query(new GetProductsQuery(true)));
    }

    [Fact]
    public async Task Invalid_input_is_rejected_with_readable_messages()
    {
        var error = await Assert.ThrowsAsync<ValidationFailedException>(() =>
            _app.Send(new CreateIngredientCommand("", IngredientKind.Ingredient, MeasureUnit.Gram, 10, 0, null, null)));

        Assert.Contains("Name", error.Errors.Keys);
        Assert.Contains("PackageSize", error.Errors.Keys);
        Assert.Contains("Размер упаковки", error.Errors["PackageSize"][0]);
    }

    [Fact]
    public async Task Product_with_manual_cost_needs_no_recipe_and_does_not_touch_stock()
    {
        var candles = await _app.Send(new CreateProductCommand(
            "Свечи для торта", ProductType.Finished, 1, MeasureUnit.Piece, 0, 5, null, [],
            CostMode.Manual, 2.5m));

        await _app.Send(new CreateBatchCommand(Monday, candles, 10, null, 4));

        var day = await _app.Query(new GetDayQuery(Monday));
        Assert.Equal(2.5m, day.Batches.Single().UnitCost);
        Assert.Equal(25m, day.Summary.TotalCosts);
        Assert.Equal(20m, day.Summary.Revenue);
        Assert.Equal(5000m, await FlourStock());

        // Такое изделие может входить в состав другого.
        var cake = await _app.Send(new CreateProductCommand(
            "Торт со свечами", ProductType.Finished, 1, MeasureUnit.Piece, 0, 30, null,
            [new RecipeLineInput(_flour, null, 1000), new RecipeLineInput(null, candles, 2)]));
        var product = await _app.Query(new GetProductQuery(cake));
        Assert.Equal(15m, product.UnitCost.Total);
    }

    [Fact]
    public async Task Recipe_product_without_lines_is_rejected()
    {
        await Assert.ThrowsAsync<ValidationFailedException>(() => _app.Send(new CreateProductCommand(
            "Пустое", ProductType.Finished, 1, MeasureUnit.Piece, 0, 5, null, [])));
    }

    [Fact]
    public async Task Copying_a_day_repeats_manual_rows_without_sales()
    {
        await _app.Send(new CreateBatchCommand(Monday, _muffin, 20, 3.5m, 12));

        Assert.Equal(1, await _app.Send(new CopyDayCommand(Monday, Tuesday)));

        var tuesday = await _app.Query(new GetDayQuery(Tuesday));
        var row = Assert.Single(tuesday.Batches);
        Assert.Equal(20m, row.Quantity);
        Assert.Equal(3.5m, row.UnitPrice);
        Assert.Equal(0m, row.Sold);
        Assert.Equal(1000m, await FlourStock());
    }

    [Fact]
    public async Task Cash_received_counts_showcase_sales_and_payments()
    {
        await _app.Send(new CreateBatchCommand(Monday, _muffin, 10, null, 4));

        var day = await _app.Query(new GetDayQuery(Monday));
        Assert.Equal(12m, day.Summary.Received);
        Assert.Equal(0m, day.Summary.ReceivedFromOrders);
    }

    [Fact]
    public async Task Overview_compares_periods_and_tracks_the_monthly_goal()
    {
        await _app.Send(new UpdateSettingsCommand(0, "сомони", 100));
        await _app.Send(new CreateBatchCommand(Wednesday, _muffin, 10, null, 10)); // прибыль 20
        await _app.Send(new CreateBatchCommand(Wednesday.AddDays(-7), _muffin, 10, null, 5)); // прибыль 5

        var overview = await _app.Query(new GetOverviewQuery(Wednesday));

        var today = overview.Periods.Single(p => p.Title == "Сегодня");
        Assert.Equal(20m, today.Profit);
        Assert.Equal(5m, today.PreviousProfit);
        Assert.Equal(300m, today.ProfitChangePercent);
        Assert.Equal(30, overview.Chart.Count);
        Assert.Equal(Wednesday, overview.Chart[^1].Date);
        Assert.Equal(30m, overview.Chart[^1].Revenue);
        Assert.NotNull(overview.Goal);
        Assert.Equal(100m, overview.Goal!.Goal);
    }

    [Fact]
    public async Task Cost_preview_tolerates_an_unfinished_draft()
    {
        var preview = await _app.Query(new PreviewProductCostQuery(
            null, 10, 0, 3,
            [new RecipeLineInput(_flour, null, 1000), new RecipeLineInput(null, null, 5), new RecipeLineInput(_flour, null, 0)]));

        Assert.Equal([10m, 0m, 0m], preview.LineCosts);
        Assert.Equal(1m, preview.UnitCost.Total);
        Assert.Equal(200m, preview.MarkupPercent);
        Assert.Equal(4, preview.BreakEvenUnits);
    }
}
