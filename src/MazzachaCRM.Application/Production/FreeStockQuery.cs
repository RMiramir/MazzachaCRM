using MazzachaCRM.Application.Abstractions;
using MazzachaCRM.Application.Common;
using Microsoft.EntityFrameworkCore;

namespace MazzachaCRM.Application.Production;

/// <summary>Готовые изделия, которые можно взять в заказ: свободный остаток одной строки дня.</summary>
public sealed record FreeStockDto(
    Guid BatchId,
    Guid ProductId,
    DateOnly ProductionDate,
    decimal Available,
    decimal UnitPrice,
    decimal UnitCost,
    bool ReservedForThisOrder);

/// <param name="ForOrderId">
/// Заказ, который сейчас редактируют: то, что за ним уже закреплено, тоже доступно для выбора.
/// </param>
/// <param name="Date">
/// День приготовления заказа: подходят изделия этого дня и остатки, перенесённые на него.
/// </param>
public sealed record GetFreeStockQuery(Guid? ForOrderId, DateOnly? Date = null) : IQuery<IReadOnlyList<FreeStockDto>>;

public sealed class GetFreeStockHandler(IReadDbContext db) : IQueryHandler<GetFreeStockQuery, IReadOnlyList<FreeStockDto>>
{
    public async Task<IReadOnlyList<FreeStockDto>> Handle(GetFreeStockQuery query, CancellationToken ct)
    {
        var batches = db.Batches
            .Include(b => b.Sales)
            .Include(b => b.WriteOffs)
            .Where(b => b.OrderId == null && b.ClosedOn == null);
        if (query.Date is { } date)
            batches = batches.Where(b => b.ProductionDate == date ||
                                         (b.ProductionDate < date && b.CarriedUntil != null && b.CarriedUntil >= date));
        var free = await batches.ToListAsync(ct);

        var rows = free
            .Where(b => b.RemainingQuantity > 0)
            .Select(b => new FreeStockDto(
                b.Id, b.ProductId, b.ProductionDate, b.RemainingQuantity, b.UnitPrice.Precise(), b.UnitCost.Precise(), false))
            .ToList();

        if (query.ForOrderId is { } orderId)
        {
            var reserved = await db.Batches
                .Where(b => b.OrderId == orderId && b.ReservedFromStock)
                .ToListAsync(ct);
            rows.AddRange(reserved.Select(b => new FreeStockDto(
                b.Id, b.ProductId, b.ProductionDate, b.Quantity, (b.PriceBeforeOrder ?? b.UnitPrice).Precise(), b.UnitCost.Precise(), true)));
        }

        return rows
            .OrderByDescending(r => r.ReservedForThisOrder)
            .ThenByDescending(r => r.ProductionDate)
            .ToList();
    }
}
