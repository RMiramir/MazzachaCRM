using MazzachaCRM.Application.Common;
using MazzachaCRM.Domain.Common;
using MazzachaCRM.Domain.Ingredients;
using MazzachaCRM.Domain.Production;
using MazzachaCRM.Domain.Products;

namespace MazzachaCRM.Application.Production;

/// <summary>
/// Общая логика изготовления: создаёт партию со снимком себестоимости и списывает
/// сырьё со склада по составу. Используется и таблицей дня, и заказами.
/// </summary>
public sealed class BatchProductionService(
    IProductionBatchRepository batches,
    IStockMovementRepository movements)
{
    public ProductionBatch Produce(
        CostingContext context,
        DateOnly date,
        Guid productId,
        decimal quantity,
        decimal? unitPrice,
        Guid? orderId = null)
    {
        var product = FindSellable(context, productId);
        var batch = ProductionBatch.Create(
            date,
            product.Id,
            quantity,
            context.Calculator.UnitCost(product.Id),
            unitPrice ?? product.SalePrice,
            orderId);

        batches.Add(batch);
        Consume(context, batch);
        return batch;
    }

    /// <summary>Списывает сырьё на партию по текущему составу изделия.</summary>
    public void Consume(CostingContext context, ProductionBatch batch)
    {
        var product = FindSellable(context, batch.ProductId);
        var note = $"{product.Name}: {batch.Quantity:0.###} шт";

        foreach (var (ingredientId, quantity) in context.Calculator.RawRequirements(product.Id, batch.Quantity))
        {
            if (quantity <= 0)
                continue;
            var ingredient = context.Ingredients[ingredientId];
            movements.Add(ingredient.ConsumeForBatch(batch.ProductionDate, quantity, batch.Id, note));
        }
    }

    /// <summary>Возвращает на склад сырьё, списанное на партию.</summary>
    public async Task RevertConsumptionAsync(CostingContext context, Guid batchId, CancellationToken ct)
    {
        foreach (var movement in await movements.ListByBatchAsync(batchId, ct))
        {
            if (context.Ingredients.TryGetValue(movement.IngredientId, out var ingredient))
                ingredient.RevertMovement(movement);
            movements.Remove(movement);
        }
    }

    private static Product FindSellable(CostingContext context, Guid productId)
    {
        if (!context.Products.TryGetValue(productId, out var product))
            throw new NotFoundException("Изделие не найдено.");
        if (product.Type != ProductType.Finished)
            throw new DomainException($"«{product.Name}» — полуфабрикат. В таблицу дня и в заказы добавляются только готовые изделия.");
        return product;
    }
}
