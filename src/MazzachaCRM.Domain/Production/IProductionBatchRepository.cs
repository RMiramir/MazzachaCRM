namespace MazzachaCRM.Domain.Production;

public interface IProductionBatchRepository
{
    /// <summary>Партия вместе с продажами и списаниями.</summary>
    Task<ProductionBatch?> GetByIdAsync(Guid id, CancellationToken ct);

    Task<List<ProductionBatch>> ListByOrderAsync(Guid orderId, CancellationToken ct);
    Task<bool> AnyForProductAsync(Guid productId, CancellationToken ct);
    void Add(ProductionBatch batch);
    void Remove(ProductionBatch batch);
}
