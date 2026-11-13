using MazzachaCRM.Application.Abstractions;
using MazzachaCRM.Application.Common;
using MazzachaCRM.Application.Expenses;
using MazzachaCRM.Application.Orders;
using MazzachaCRM.Domain.Orders;
using MazzachaCRM.Domain.Production;
using Microsoft.EntityFrameworkCore;

namespace MazzachaCRM.Application.Production;

public sealed record SaleEntryDto(decimal Quantity, decimal UnitPrice);

/// <summary>Строка таблицы дня: партия, изготовленная в этот день. Все цифры на вечер этого дня.</summary>
public sealed record DayBatchDto(
    Guid Id,
    Guid ProductId,
    string ProductName,
    decimal Quantity,
    decimal UnitCost,
    CostDto UnitCostBreakdown,
    decimal UnitPrice,
    decimal Sold,
    decimal WrittenOff,
    decimal Remaining,
    decimal TotalCost,
    decimal PotentialRevenue,
    decimal PotentialProfit,
    decimal ActualRevenue,
    decimal ProfitOnSold,
    IReadOnlyList<SaleEntryDto> Sales,
    string? WriteOffReason,
    decimal SoldLater,
    decimal WrittenOffLater,
    int? BreakEvenUnits,
    Guid? OrderId,
    int? OrderNumber,
    bool OrderIsConsignment,
    decimal OrderReturned);

/// <summary>Остаток прошлых дней, который можно продать или списать сегодня.</summary>
public sealed record DayCarryOverDto(
    Guid Id,
    Guid ProductId,
    string ProductName,
    DateOnly ProductionDate,
    decimal UnitCost,
    decimal UnitPrice,
    decimal RemainingAtStart,
    decimal Sold,
    decimal WrittenOff,
    decimal Remaining,
    decimal Revenue,
    decimal ProfitOnSold,
    IReadOnlyList<SaleEntryDto> Sales,
    string? WriteOffReason,
    Guid? OrderId,
    int? OrderNumber,
    bool OrderIsConsignment);

public sealed record DaySummaryDto(
    decimal ProductionCost,
    decimal LaborCost,
    decimal Expenses,
    decimal TotalCosts,
    decimal Revenue,
    decimal Profit,
    decimal PotentialRevenue,
    decimal PotentialProfit,
    decimal ProfitOnSold,
    decimal WriteOffLoss,
    decimal UnsoldValueAtPrice,
    decimal UnsoldValueAtCost,
    decimal Received,
    decimal ReceivedFromOrders,
    decimal DebtsTotal);

public sealed record DayDto(
    DateOnly Date,
    DaySummaryDto Summary,
    IReadOnlyList<DayBatchDto> Batches,
    IReadOnlyList<DayCarryOverDto> CarryOver,
    IReadOnlyList<ExpenseDto> Expenses,
    IReadOnlyList<OrderDto> Orders);

public sealed record GetDayQuery(DateOnly Date) : IQuery<DayDto>;

public sealed class GetDayHandler(IReadDbContext db, CostingContextLoader costing) : IQueryHandler<GetDayQuery, DayDto>
{
    public async Task<DayDto> Handle(GetDayQuery query, CancellationToken ct)
    {
        var date = query.Date;

        // Партии этого дня и незакрытые к его началу партии прошлых дней.
        var batches = await db.Batches
            .Include(b => b.Sales)
            .Include(b => b.WriteOffs)
            .Where(b => b.ProductionDate == date ||
                        (b.ProductionDate < date && (b.ClosedOn == null || b.ClosedOn >= date)))
            .ToListAsync(ct);

        var productNames = await db.Products.ToDictionaryAsync(p => p.Id, p => p.Name, ct);
        var expenses = await db.Expenses.Where(e => e.Date == date).OrderBy(e => e.Name).ToListAsync(ct);

        // Заказы, которые нужно выдать в этот день или которые в этот день изготовлены.
        var dayOrders = await db.Orders
            .Include(o => o.Items)
            .Include(o => o.Payments)
            .Where(o => o.Status != OrderStatus.Cancelled && (o.DueDate == date || o.ProducedDate == date))
            .ToListAsync(ct);

        // Заказы, к которым относятся партии на экране, и все неоплаченные — для подписей и долгов.
        var batchOrderIds = batches.Where(b => b.OrderId != null).Select(b => b.OrderId!.Value).Distinct().ToList();
        var relatedOrders = await db.Orders
            .Include(o => o.Items)
            .Include(o => o.Payments)
            .Where(o => batchOrderIds.Contains(o.Id) ||
                        o.Status == OrderStatus.Ready || o.Status == OrderStatus.Delivered)
            .ToListAsync(ct);
        var ordersById = relatedOrders.ToDictionary(o => o.Id);
        var customerIds = relatedOrders.Select(o => o.CustomerId).Distinct().ToList();
        var consignment = (await db.Customers
                .Where(c => customerIds.Contains(c.Id) && c.IsConsignment)
                .Select(c => c.Id)
                .ToListAsync(ct))
            .ToHashSet();

        var payments = await db.Orders
            .SelectMany(o => o.Payments)
            .Where(p => p.Date == date)
            .ToListAsync(ct);

        var today = batches.Where(b => b.ProductionDate == date).OrderBy(b => b.OrderId != null).ThenBy(b => b.CreatedAt).ToList();
        var carryOver = batches
            .Where(b => b.ProductionDate < date && b.RemainingAtStartOf(date) > 0)
            .OrderBy(b => b.ProductionDate)
            .ThenBy(b => Name(b.ProductId))
            .ToList();

        var productionCost = today.Sum(b => b.TotalCost);
        var expensesTotal = expenses.Sum(e => e.Amount);
        var totalCosts = productionCost + expensesTotal;
        var all = today.Concat(carryOver).ToList();
        var revenue = all.Sum(b => b.RevenueOn(date));
        var potentialRevenue = today.Sum(PotentialRevenue);

        // Деньги на руках: оплаты по заказам и всё, что продано с витрины.
        // Продажа заказа в день готовности — это ещё не деньги, они придут оплатой.
        var receivedFromOrders = payments.Sum(p => p.Amount);
        var cashSales = all.Sum(b => b.Sales
            .Where(s => s.Date == date && !(b.OrderId != null && s.Date == b.ProductionDate))
            .Sum(s => s.Amount));

        var summary = new DaySummaryDto(
            productionCost.Money(),
            today.Sum(b => b.LaborCost).Money(),
            expensesTotal.Money(),
            totalCosts.Money(),
            revenue.Money(),
            (revenue - totalCosts).Money(),
            potentialRevenue.Money(),
            (potentialRevenue - totalCosts).Money(),
            all.Sum(b => b.RevenueOn(date) - b.SoldOn(date) * b.UnitCost).Money(),
            all.Sum(b => b.WrittenOffOn(date) * b.UnitCost).Money(),
            all.Sum(b => b.RemainingAtEndOf(date) * b.UnitPrice).Money(),
            all.Sum(b => b.RemainingAtEndOf(date) * b.UnitCost).Money(),
            (receivedFromOrders + cashSales).Money(),
            receivedFromOrders.Money(),
            relatedOrders.Where(o => o.IsProduced && o.Balance > 0).Sum(o => o.Balance).Money());

        var mapper = await OrderMapper.CreateAsync(db, costing, dayOrders, ct);
        var orderRows = dayOrders
            .OrderBy(o => o.Status == OrderStatus.Delivered)
            .ThenBy(o => o.DueTime ?? TimeOnly.MaxValue)
            .ThenBy(o => o.Number)
            .Select(mapper.Map)
            .ToList();

        return new DayDto(
            date,
            summary,
            today.Select(ToBatchRow).ToList(),
            carryOver.Select(ToCarryOverRow).ToList(),
            expenses.Select(e => new ExpenseDto(e.Id, e.Date, e.Name, e.Amount.Money())).ToList(),
            orderRows);

        string Name(Guid productId) => productNames.GetValueOrDefault(productId, "Удалённое изделие");

        Order? OrderOf(ProductionBatch b) =>
            b.OrderId is { } id && ordersById.TryGetValue(id, out var order) ? order : null;

        bool IsShop(ProductionBatch b) => OrderOf(b) is { } order && consignment.Contains(order.CustomerId);

        decimal Returned(ProductionBatch b) =>
            OrderOf(b)?.Items.FirstOrDefault(i => i.Id == b.OrderItemId)?.ReturnedQuantity ?? 0;

        decimal PotentialRevenue(ProductionBatch b) => b.RevenueOn(date) + b.RemainingAtEndOf(date) * b.UnitPrice;

        IReadOnlyList<SaleEntryDto> SalesOn(ProductionBatch b) =>
            b.Sales.Where(s => s.Date == date).Select(s => new SaleEntryDto(s.Quantity, s.UnitPrice.Precise())).ToList();

        DayBatchDto ToBatchRow(ProductionBatch b)
        {
            var sold = b.SoldOn(date);
            var actualRevenue = b.RevenueOn(date);
            var potential = PotentialRevenue(b);
            return new DayBatchDto(
                b.Id,
                b.ProductId,
                Name(b.ProductId),
                b.Quantity,
                b.UnitCost.Precise(),
                CostDto.From(b.UnitCostBreakdown),
                b.UnitPrice.Precise(),
                sold,
                b.WrittenOffOn(date),
                b.RemainingAtEndOf(date),
                b.TotalCost.Money(),
                potential.Money(),
                (potential - b.TotalCost).Money(),
                actualRevenue.Money(),
                (actualRevenue - sold * b.UnitCost).Money(),
                SalesOn(b),
                b.WriteOffs.FirstOrDefault(w => w.Date == date)?.Reason,
                b.SoldAfter(date),
                b.WrittenOffAfter(date),
                Rounding.BreakEvenUnits(b.TotalCost, b.UnitPrice),
                b.OrderId,
                OrderOf(b)?.Number,
                IsShop(b),
                Returned(b));
        }

        DayCarryOverDto ToCarryOverRow(ProductionBatch b)
        {
            var sold = b.SoldOn(date);
            var revenueOnDate = b.RevenueOn(date);
            return new DayCarryOverDto(
                b.Id,
                b.ProductId,
                Name(b.ProductId),
                b.ProductionDate,
                b.UnitCost.Precise(),
                b.UnitPrice.Precise(),
                b.RemainingAtStartOf(date),
                sold,
                b.WrittenOffOn(date),
                b.RemainingAtEndOf(date),
                revenueOnDate.Money(),
                (revenueOnDate - sold * b.UnitCost).Money(),
                SalesOn(b),
                b.WriteOffs.FirstOrDefault(w => w.Date == date)?.Reason,
                b.OrderId,
                OrderOf(b)?.Number,
                IsShop(b));
        }
    }
}
