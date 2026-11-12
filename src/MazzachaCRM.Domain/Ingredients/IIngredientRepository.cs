namespace MazzachaCRM.Domain.Ingredients;

public interface IIngredientRepository
{
    Task<Ingredient?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<List<Ingredient>> ListAsync(CancellationToken ct);
    Task<bool> NameExistsAsync(string name, Guid? exceptId, CancellationToken ct);
    void Add(Ingredient ingredient);
    void Remove(Ingredient ingredient);
}

public interface IStockMovementRepository
{
    Task<StockMovement?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<List<StockMovement>> ListByBatchAsync(Guid batchId, CancellationToken ct);
    Task<bool> AnyForIngredientAsync(Guid ingredientId, CancellationToken ct);
    void Add(StockMovement movement);
    void Remove(StockMovement movement);
}
