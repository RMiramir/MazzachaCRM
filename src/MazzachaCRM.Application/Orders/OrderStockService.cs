using MazzachaCRM.Domain.Common;
using MazzachaCRM.Domain.Ingredients;
using MazzachaCRM.Domain.Orders;
using MazzachaCRM.Domain.Production;

namespace MazzachaCRM.Application.Orders;

/// <summary>
/// Готовые изделия в заказ: закрепляет за позицией заказа то, что уже приготовлено,
/// и возвращает обратно на витрину, если заказ изменили или отменили.
/// Сырьё при этом повторно не списывается: расход просто делится между партиями.
/// </summary>
public sealed class OrderStockService(IProductionBatchRepository batches, IStockMovementRepository movements)
{
    /// <summary>Закрепляет за позицией заказа изделия из свободной партии.</summary>
    public async Task<ProductionBatch> ReserveAsync(Order order, OrderItem item, Guid fromBatchId, CancellationToken ct)
    {
        var source = await batches.GetByIdAsync(fromBatchId, ct)
            ?? throw new DomainException("Готовые изделия не найдены: возможно, эту строку дня уже удалили.");
        if (source.ProductId != item.ProductId)
            throw new DomainException("Выбранные готовые изделия относятся к другому изделию.");

        var quantityBefore = source.Quantity;
        var reserved = source.ReserveForOrder(item.Quantity, order.Id, item.Id, item.UnitPrice);
        if (ReferenceEquals(reserved, source))
            return reserved;

        // Отделили часть: ей достаётся такая же доля списанного сырья.
        batches.Add(reserved);
        var share = item.Quantity / quantityBefore;
        foreach (var movement in await movements.ListByBatchAsync(source.Id, ct))
            movements.Add(movement.SplitForBatch(share, reserved.Id));

        return reserved;
    }

    /// <summary>
    /// Возвращает освобождённые изделия в ту строку, от которой их отделяли, если она ещё свободна.
    /// Иначе они остаются на витрине отдельной строкой.
    /// </summary>
    public async Task MergeBackAsync(IEnumerable<ProductionBatch> released, CancellationToken ct)
    {
        foreach (var part in released)
        {
            if (part.OrderId is not null || part.SplitFromBatchId is not { } sourceId)
                continue;

            var source = await batches.GetByIdAsync(sourceId, ct);
            if (source is null || !source.CanAbsorb(part))
                continue;

            source.Absorb(part);
            foreach (var movement in await movements.ListByBatchAsync(part.Id, ct))
                movement.MoveToBatch(source.Id);
            batches.Remove(part);
        }
    }
}
