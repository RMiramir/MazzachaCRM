namespace MazzachaCRM.Domain.Products;

public interface IProductRepository
{
    /// <summary>Изделие вместе с составом.</summary>
    Task<Product?> GetByIdAsync(Guid id, CancellationToken ct);

    /// <summary>Все изделия вместе с составами: нужны для расчёта себестоимости.</summary>
    Task<List<Product>> ListAsync(CancellationToken ct);

    Task<bool> NameExistsAsync(string name, Guid? exceptId, CancellationToken ct);
    Task<List<string>> ListNamesUsingIngredientAsync(Guid ingredientId, CancellationToken ct);
    Task<List<string>> ListNamesUsingProductAsync(Guid componentProductId, CancellationToken ct);
    void Add(Product product);
    void Remove(Product product);
}
