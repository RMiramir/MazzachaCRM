using MazzachaCRM.Application.Customers;
using MazzachaCRM.Application.Ingredients;
using MazzachaCRM.Application.Production;
using MazzachaCRM.Application.Products;
using MazzachaCRM.Application.Reports;

namespace MazzachaCRM.Application.Tests;

/// <summary>
/// Контрольный пример: три дня из исходного расчёта владельца кондитерской.
/// Цифры в таблице дня и в сводке должны совпасть с посчитанными вручную.
/// </summary>
public class DemoScenarioTests : IAsyncLifetime
{
    private static readonly DateOnly Day3 = new(2026, 11, 12);
    private static readonly DateOnly Day2 = Day3.AddDays(-1);
    private static readonly DateOnly Day1 = Day3.AddDays(-2);

    private TestApp _app = null!;

    public async Task InitializeAsync()
    {
        _app = await TestApp.CreateAsync();
        Assert.True(await _app.SeedDemo(Day3));
    }

    public async Task DisposeAsync() => await _app.DisposeAsync();

    [Fact]
    public async Task Unit_costs_match_the_manual_calculation()
    {
        var products = await _app.Query(new GetProductsQuery(false));
        decimal Cost(string name) => products.Single(p => p.Name == name).UnitCost!.Total;

        Assert.Equal(5m, Cost("Мини-чизкейк «Сердце»"));
        Assert.Equal(7.2m, Cost("Торт в формочке"));
        Assert.Equal(13m, Cost("Трайфл"));
        Assert.Equal(1.3m, Cost("Маффин"));
        Assert.Equal(4.5m, Cost("Медовый торт (кусок)"));
        Assert.Equal(52m, Cost("Медовый торт (целый)"));
    }

    [Fact]
    public async Task Day_one_matches_the_manual_table()
    {
        var day = await _app.Query(new GetDayQuery(Day1));

        Assert.Equal(198m, day.Summary.TotalCosts);
        Assert.Equal(270m, day.Summary.Revenue);
        Assert.Equal(72m, day.Summary.Profit);

        var cheesecake = day.Batches.Single(b => b.ProductName.StartsWith("Мини-чизкейк"));
        Assert.Equal(100m, cheesecake.TotalCost);
        Assert.Equal(90m, cheesecake.ActualRevenue);
        Assert.Equal(12m, cheesecake.Sold);
        Assert.Equal(8m, cheesecake.WrittenOff);
        Assert.Equal(0m, cheesecake.Remaining);
        Assert.Equal(14, cheesecake.BreakEvenUnits); // чтобы окупиться, нужно продать 14 из 20

        Assert.Equal(40m, day.Summary.WriteOffLoss);  // 8 чизкейков по себестоимости 5
    }

    [Fact]
    public async Task Day_two_matches_the_manual_table()
    {
        var day = await _app.Query(new GetDayQuery(Day2));

        Assert.Equal(109.9m, day.Summary.TotalCosts);
        Assert.Equal(210.75m, day.Summary.Revenue);
        Assert.Equal(100.85m, day.Summary.Profit);
        Assert.Empty(day.CarryOver);
    }

    [Fact]
    public async Task Day_three_matches_the_manual_table()
    {
        var day = await _app.Query(new GetDayQuery(Day3));

        Assert.Equal(470m, day.Summary.TotalCosts);
        Assert.Equal(774m, day.Summary.PotentialRevenue);
        Assert.Equal(304m, day.Summary.PotentialProfit);
        Assert.Equal(128.5m, day.Summary.Revenue);
        Assert.Equal(63m, day.Summary.ProfitOnSold);
        Assert.Equal(-341.5m, day.Summary.Profit);
        Assert.Equal(645.5m, day.Summary.UnsoldValueAtPrice);

        var trifle = day.Batches.Single(b => b.ProductName == "Трайфл");
        Assert.Equal(28m, trifle.Quantity);
        Assert.Equal(364m, trifle.TotalCost);
        Assert.Equal(560m, trifle.PotentialRevenue);
        Assert.Equal(196m, trifle.PotentialProfit);
        Assert.Equal(0m, trifle.ActualRevenue);

        var slices = day.Batches.Single(b => b.ProductName == "Медовый торт (кусок)");
        Assert.Equal(54m, slices.TotalCost);
        Assert.Equal(114m, slices.PotentialRevenue);
        Assert.Equal(60m, slices.PotentialProfit);
        Assert.Equal(28.5m, slices.ActualRevenue);
        Assert.Equal(15m, slices.ProfitOnSold);

        var whole = day.Batches.Single(b => b.ProductName == "Медовый торт (целый)");
        Assert.Equal(48m, whole.PotentialProfit);
    }

    [Fact]
    public async Task Period_summary_matches_the_manual_table()
    {
        var report = await _app.Query(new GetPeriodReportQuery(Day1, Day3));

        Assert.Equal(3, report.Days.Count);
        Assert.Equal(777.9m, report.Totals.TotalCosts);
        Assert.Equal(609.25m, report.Totals.Revenue);
        Assert.Equal(-168.65m, report.Totals.Profit);
        Assert.Equal(476.85m, report.Totals.PotentialProfit); // если допродать всё с третьего дня

        var trifle = report.Products.Single(p => p.Name == "Трайфл");
        Assert.Equal(32m, trifle.Made);
        Assert.Equal(4m, trifle.Sold);
    }

    [Fact]
    public async Task Margins_match_the_manual_table()
    {
        var margins = await _app.Query(new GetMarginReportQuery());
        MarginRowDto Row(string name) => margins.Single(m => m.Name == name);

        Assert.Equal(150m, Row("Маффин").MarkupPercent);
        Assert.Equal(1.95m, Row("Маффин").ProfitPerUnit);
        Assert.Equal(111.1m, Row("Медовый торт (кусок)").MarkupPercent);
        Assert.Equal(94.4m, Row("Торт в формочке").MarkupPercent);
        Assert.Equal(92.3m, Row("Медовый торт (целый)").MarkupPercent);
        Assert.Equal(53.8m, Row("Трайфл").MarkupPercent);
        Assert.Equal(50m, Row("Мини-чизкейк «Сердце»").MarkupPercent);
        Assert.Equal("Маффин", margins[0].Name); // самая высокая наценка сверху
    }

    [Fact]
    public async Task Stock_is_reduced_by_what_went_into_production()
    {
        var ingredients = await _app.Query(new GetIngredientsQuery());
        decimal Stock(string name) => ingredients.Single(i => i.Name == name).StockQuantity;

        // Сливки: закуплено 3000 мл; чизкейк 100, торт в формочке 400, 32 трайфла = 8 партий по 300.
        Assert.Equal(100m, Stock("Сливки 33%"));
        // Стаканы: закуплено 50, ушло 32.
        Assert.Equal(18m, Stock("Стакан для трайфла"));
        // Мука: 5000 − 325 − 500 − три основы по 600 − 500 на поставку маффинов в магазин.
        Assert.Equal(1875m, Stock("Мука"));

        Assert.True(ingredients.Single(i => i.Name == "Сливки 33%").IsLowStock);
        Assert.True(ingredients.Single(i => i.Name == "Стакан для трайфла").IsLowStock);
    }

    [Fact]
    public async Task Shop_shipment_leaves_a_debt_for_the_unpaid_part()
    {
        var day = await _app.Query(new GetDayQuery(Day3));

        // 20 проданных маффинов по 3,25 = 65, магазин заплатил 30.
        Assert.Equal(35m, day.Summary.DebtsTotal);
        var shop = Assert.Single(await _app.Query(new GetCustomersQuery("Магазин")));
        Assert.True(shop.IsConsignment);
        Assert.Equal(35m, shop.Debt);
    }

    [Fact]
    public async Task Seeding_twice_does_nothing()
    {
        Assert.False(await _app.SeedDemo(Day3));
    }
}
