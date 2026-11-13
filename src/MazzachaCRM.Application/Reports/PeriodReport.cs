using FluentValidation;
using MazzachaCRM.Application.Abstractions;
using MazzachaCRM.Application.Common;
using Microsoft.EntityFrameworkCore;

namespace MazzachaCRM.Application.Reports;

public sealed record PeriodDayDto(
    DateOnly Date,
    decimal ProductionCost,
    decimal Expenses,
    decimal TotalCosts,
    decimal Revenue,
    decimal Profit,
    decimal WriteOffLoss,
    decimal Received);

public sealed record PeriodTotalsDto(
    decimal ProductionCost,
    decimal LaborCost,
    decimal Expenses,
    decimal TotalCosts,
    decimal Revenue,
    decimal Profit,
    decimal ProfitOnSold,
    decimal WriteOffLoss,
    decimal UnsoldValueAtPrice,
    decimal UnsoldValueAtCost,
    decimal PotentialProfit,
    decimal Received);

public sealed record PeriodProductDto(
    Guid ProductId,
    string Name,
    decimal Made,
    decimal Sold,
    decimal WrittenOff,
    decimal Revenue,
    decimal CostOfSold,
    decimal ProfitOnSold);

public sealed record PeriodReportDto(
    DateOnly From,
    DateOnly To,
    IReadOnlyList<PeriodDayDto> Days,
    PeriodTotalsDto Totals,
    IReadOnlyList<PeriodProductDto> Products);

public sealed record GetPeriodReportQuery(DateOnly From, DateOnly To) : IQuery<PeriodReportDto>;

public sealed class GetPeriodReportValidator : AbstractValidator<GetPeriodReportQuery>
{
    public GetPeriodReportValidator()
    {
        RuleFor(x => x.To)
            .GreaterThanOrEqualTo(x => x.From)
            .WithMessage("Конец периода не может быть раньше начала.");
    }
}

public sealed class GetPeriodReportHandler(IReadDbContext db) : IQueryHandler<GetPeriodReportQuery, PeriodReportDto>
{
    private sealed class DayTotals
    {
        public decimal ProductionCost;
        public decimal LaborCost;
        public decimal Expenses;
        public decimal Revenue;
        public decimal ProfitOnSold;
        public decimal WriteOffLoss;
        public decimal Received;
    }

    private sealed class ProductTotals
    {
        public decimal Made;
        public decimal Sold;
        public decimal WrittenOff;
        public decimal Revenue;
        public decimal CostOfSold;
    }

    public async Task<PeriodReportDto> Handle(GetPeriodReportQuery query, CancellationToken ct)
    {
        var (from, to) = (query.From, query.To);

        // Партии, изготовленные до конца периода и ещё не закрытые к его началу.
        var batches = await db.Batches
            .Include(b => b.Sales)
            .Include(b => b.WriteOffs)
            .Where(b => b.ProductionDate <= to &&
                        (b.ProductionDate >= from || b.ClosedOn == null || b.ClosedOn >= from))
            .ToListAsync(ct);

        var expenses = await db.Expenses.Where(e => e.Date >= from && e.Date <= to).ToListAsync(ct);
        var productNames = await db.Products.ToDictionaryAsync(p => p.Id, p => p.Name, ct);
        var payments = await db.Orders
            .SelectMany(o => o.Payments)
            .Where(p => p.Date >= from && p.Date <= to)
            .ToListAsync(ct);

        var days = new SortedDictionary<DateOnly, DayTotals>();
        var products = new Dictionary<Guid, ProductTotals>();

        foreach (var batch in batches)
        {
            var product = Product(batch.ProductId);

            if (InPeriod(batch.ProductionDate))
            {
                var day = Day(batch.ProductionDate);
                day.ProductionCost += batch.TotalCost;
                day.LaborCost += batch.LaborCost;
                product.Made += batch.Quantity;
            }

            foreach (var sale in batch.Sales.Where(s => InPeriod(s.Date)))
            {
                var costOfSold = sale.Quantity * batch.UnitCost;
                var day = Day(sale.Date);
                day.Revenue += sale.Amount;
                day.ProfitOnSold += sale.Amount - costOfSold;
                // Продажа заказа в день готовности — это долг клиента, деньги придут оплатой.
                if (!(batch.OrderId != null && sale.Date == batch.ProductionDate))
                    day.Received += sale.Amount;
                product.Sold += sale.Quantity;
                product.Revenue += sale.Amount;
                product.CostOfSold += costOfSold;
            }

            foreach (var writeOff in batch.WriteOffs.Where(w => InPeriod(w.Date)))
            {
                Day(writeOff.Date).WriteOffLoss += writeOff.Quantity * batch.UnitCost;
                product.WrittenOff += writeOff.Quantity;
            }
        }

        foreach (var expense in expenses)
            Day(expense.Date).Expenses += expense.Amount;

        foreach (var payment in payments)
            Day(payment.Date).Received += payment.Amount;

        var dayRows = days
            .Select(pair =>
            {
                var d = pair.Value;
                var totalCosts = d.ProductionCost + d.Expenses;
                return new PeriodDayDto(
                    pair.Key,
                    d.ProductionCost.Money(),
                    d.Expenses.Money(),
                    totalCosts.Money(),
                    d.Revenue.Money(),
                    (d.Revenue - totalCosts).Money(),
                    d.WriteOffLoss.Money(),
                    d.Received.Money());
            })
            .ToList();

        var productionCost = days.Values.Sum(d => d.ProductionCost);
        var expensesTotal = days.Values.Sum(d => d.Expenses);
        var revenue = days.Values.Sum(d => d.Revenue);
        var profit = revenue - productionCost - expensesTotal;
        var unsoldAtPrice = batches.Sum(b => b.RemainingAtEndOf(to) * b.UnitPrice);

        var totals = new PeriodTotalsDto(
            productionCost.Money(),
            days.Values.Sum(d => d.LaborCost).Money(),
            expensesTotal.Money(),
            (productionCost + expensesTotal).Money(),
            revenue.Money(),
            profit.Money(),
            days.Values.Sum(d => d.ProfitOnSold).Money(),
            days.Values.Sum(d => d.WriteOffLoss).Money(),
            unsoldAtPrice.Money(),
            batches.Sum(b => b.RemainingAtEndOf(to) * b.UnitCost).Money(),
            (profit + unsoldAtPrice).Money(),
            days.Values.Sum(d => d.Received).Money());

        var productRows = products
            .Where(pair => pair.Value.Made != 0 || pair.Value.Sold != 0 || pair.Value.WrittenOff != 0)
            .Select(pair => new PeriodProductDto(
                pair.Key,
                productNames.GetValueOrDefault(pair.Key, "Удалённое изделие"),
                pair.Value.Made,
                pair.Value.Sold,
                pair.Value.WrittenOff,
                pair.Value.Revenue.Money(),
                pair.Value.CostOfSold.Money(),
                (pair.Value.Revenue - pair.Value.CostOfSold).Money()))
            .OrderByDescending(p => p.ProfitOnSold)
            .ToList();

        return new PeriodReportDto(from, to, dayRows, totals, productRows);

        bool InPeriod(DateOnly date) => date >= from && date <= to;

        DayTotals Day(DateOnly date)
        {
            if (!days.TryGetValue(date, out var day))
                days[date] = day = new DayTotals();
            return day;
        }

        ProductTotals Product(Guid id)
        {
            if (!products.TryGetValue(id, out var product))
                products[id] = product = new ProductTotals();
            return product;
        }
    }
}
