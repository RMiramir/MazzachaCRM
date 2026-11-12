using MazzachaCRM.Domain.Common;
using MazzachaCRM.Domain.Production;
using MazzachaCRM.Domain.Products;

namespace MazzachaCRM.Domain.Tests;

public class ProductionBatchTests
{
    private static readonly DateOnly Monday = new(2026, 11, 9);
    private static readonly DateOnly Tuesday = Monday.AddDays(1);
    private static readonly DateOnly Wednesday = Monday.AddDays(2);

    private static ProductionBatch Batch(decimal quantity = 20, decimal unitCost = 5, decimal price = 7.5m) =>
        ProductionBatch.Create(Monday, Guid.NewGuid(), quantity, new CostBreakdown(unitCost, 0, 0), price);

    [Fact]
    public void Cost_and_revenue_follow_the_day_table_formulas()
    {
        // 20 чизкейков по себестоимости 5, цена 7,5, продано 12.
        var batch = Batch();
        batch.SetSalesForDate(Monday, [new SaleEntry(12, 7.5m)]);

        Assert.Equal(100m, batch.TotalCost);
        Assert.Equal(90m, batch.ActualRevenue);
        Assert.Equal(8m, batch.RemainingQuantity);
        Assert.Equal(12m, batch.SoldOn(Monday));
    }

    [Fact]
    public void Leftovers_carry_over_and_can_be_sold_on_later_days()
    {
        var batch = Batch();
        batch.SetSalesForDate(Monday, [new SaleEntry(12, 7.5m)]);
        batch.SetSalesForDate(Tuesday, [new SaleEntry(5, 6m)]);

        Assert.Equal(8m, batch.RemainingAtStartOf(Tuesday));
        Assert.Equal(3m, batch.RemainingAtEndOf(Tuesday));
        Assert.Equal(30m, batch.RevenueOn(Tuesday));
        Assert.Equal(5m, batch.SoldAfter(Monday));
        Assert.Null(batch.ClosedOn);
    }

    [Fact]
    public void Batch_closes_on_the_day_the_last_piece_is_sold_or_written_off()
    {
        var batch = Batch();
        batch.SetSalesForDate(Monday, [new SaleEntry(12, 7.5m)]);
        batch.SetWriteOffForDate(Wednesday, 8, "Испортились");

        Assert.Equal(0m, batch.RemainingQuantity);
        Assert.Equal(Wednesday, batch.ClosedOn);

        // Отмена списания снова открывает партию.
        batch.SetWriteOffForDate(Wednesday, 0, null);
        Assert.Null(batch.ClosedOn);
    }

    [Fact]
    public void Cannot_sell_or_write_off_more_than_is_left()
    {
        var batch = Batch();
        batch.SetSalesForDate(Monday, [new SaleEntry(12, 7.5m)]);

        Assert.Throws<DomainException>(() => batch.SetSalesForDate(Tuesday, [new SaleEntry(9, 7.5m)]));
        Assert.Throws<DomainException>(() => batch.SetWriteOffForDate(Tuesday, 9, null));
        Assert.Throws<DomainException>(() => batch.ChangeQuantity(11));
    }

    [Fact]
    public void Sales_before_the_production_day_are_rejected()
    {
        var batch = Batch();

        Assert.Throws<DomainException>(() => batch.SetSalesForDate(Monday.AddDays(-1), [new SaleEntry(1, 7.5m)]));
    }

    [Fact]
    public void Setting_sales_for_a_day_replaces_that_day_only()
    {
        var batch = Batch();
        batch.SetSalesForDate(Monday, [new SaleEntry(12, 7.5m)]);
        batch.SetSalesForDate(Tuesday, [new SaleEntry(2, 7.5m)]);

        batch.SetSalesForDate(Monday, [new SaleEntry(10, 7.5m), new SaleEntry(4, 5m)]);

        Assert.Equal(14m, batch.SoldOn(Monday));
        Assert.Equal(95m, batch.RevenueOn(Monday));
        Assert.Equal(2m, batch.SoldOn(Tuesday));
    }

    [Fact]
    public void Changing_the_price_updates_sales_made_at_the_regular_price_only()
    {
        var batch = Batch();
        batch.SetSalesForDate(Monday, [new SaleEntry(10, 7.5m), new SaleEntry(2, 5m)]);

        batch.ChangePrice(8m);

        Assert.Equal(8m, batch.UnitPrice);
        Assert.Equal(90m, batch.ActualRevenue); // 10 × 8 + 2 × 5
    }

    [Fact]
    public void Cost_snapshot_does_not_lose_precision_for_fractional_unit_cost()
    {
        // Торт за 52 режется на 12 кусков: 12 × (52 / 12) должно снова дать 52 после округления.
        var batch = ProductionBatch.Create(Monday, Guid.NewGuid(), 12, new CostBreakdown(52m / 12, 0, 0), 9.5m);

        Assert.Equal(52m, Math.Round(batch.TotalCost, 2));
    }
}
