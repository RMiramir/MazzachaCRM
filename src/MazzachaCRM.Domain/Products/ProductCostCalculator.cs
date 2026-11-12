using MazzachaCRM.Domain.Common;
using MazzachaCRM.Domain.Ingredients;

namespace MazzachaCRM.Domain.Products;

/// <summary>Себестоимость с разбивкой по статьям.</summary>
public readonly record struct CostBreakdown(decimal Ingredients, decimal Packaging, decimal Labor)
{
    public static readonly CostBreakdown Zero = new(0, 0, 0);

    public decimal Total => Ingredients + Packaging + Labor;

    public static CostBreakdown operator +(CostBreakdown a, CostBreakdown b) =>
        new(a.Ingredients + b.Ingredients, a.Packaging + b.Packaging, a.Labor + b.Labor);

    public static CostBreakdown operator *(CostBreakdown a, decimal k) =>
        new(a.Ingredients * k, a.Packaging * k, a.Labor * k);

    public static CostBreakdown operator /(CostBreakdown a, decimal k) =>
        new(a.Ingredients / k, a.Packaging / k, a.Labor / k);
}

/// <summary>
/// Доменный сервис: считает себестоимость изделий по составу, включая вложенные
/// полуфабрикаты, и раскладывает изделие до сырья для списания со склада.
/// Работает на снимке данных, переданном в конструктор.
/// </summary>
public sealed class ProductCostCalculator
{
    private readonly IReadOnlyDictionary<Guid, Product> _products;
    private readonly IReadOnlyDictionary<Guid, Ingredient> _ingredients;
    private readonly decimal _hourlyRate;
    private readonly Dictionary<Guid, CostBreakdown> _batchCostCache = [];

    public ProductCostCalculator(
        IReadOnlyDictionary<Guid, Product> products,
        IReadOnlyDictionary<Guid, Ingredient> ingredients,
        decimal hourlyRate)
    {
        _products = products;
        _ingredients = ingredients;
        _hourlyRate = hourlyRate;
    }

    /// <summary>Стоимость одной партии сохранённого изделия.</summary>
    public CostBreakdown BatchCost(Guid productId) => BatchCost(productId, []);

    /// <summary>Себестоимость одной единицы сохранённого изделия.</summary>
    public CostBreakdown UnitCost(Guid productId) => BatchCost(productId) / Product(productId).YieldQuantity;

    /// <summary>
    /// Стоимость партии для черновика, который ещё не сохранён (живой пересчёт в редакторе).
    /// </summary>
    public CostBreakdown DraftBatchCost(Guid? productId, IEnumerable<RecipeLineSpec> lines, decimal laborMinutes)
    {
        var path = new HashSet<Guid>();
        if (productId is { } id)
            path.Add(id);

        var total = LaborCost(laborMinutes);
        foreach (var line in lines)
            total += LineCost(line, path);
        return total;
    }

    /// <summary>Стоимость одной строки состава.</summary>
    public CostBreakdown LineCost(RecipeLineSpec line, Guid? ownerProductId = null)
    {
        var path = new HashSet<Guid>();
        if (ownerProductId is { } id)
            path.Add(id);
        return LineCost(line, path);
    }

    /// <summary>Сколько сырья нужно на заданное количество изделия: продукт → количество.</summary>
    public IReadOnlyDictionary<Guid, decimal> RawRequirements(Guid productId, decimal quantity)
    {
        var result = new Dictionary<Guid, decimal>();
        Collect(productId, quantity, result, []);
        return result;
    }

    private CostBreakdown BatchCost(Guid productId, HashSet<Guid> path)
    {
        if (_batchCostCache.TryGetValue(productId, out var cached))
            return cached;

        var product = Product(productId);
        if (!path.Add(productId))
            throw new DomainException($"Состав зациклен: изделие «{product.Name}» входит само в себя через другие изделия.");

        var total = LaborCost(product.LaborMinutes);
        foreach (var line in product.Lines)
            total += LineCost(new RecipeLineSpec(line.IngredientId, line.ComponentProductId, line.Quantity), path);

        path.Remove(productId);
        _batchCostCache[productId] = total;
        return total;
    }

    private CostBreakdown LineCost(RecipeLineSpec line, HashSet<Guid> path)
    {
        if (line.IngredientId is { } ingredientId)
        {
            var ingredient = Ingredient(ingredientId);
            var cost = ingredient.UnitPrice * line.Quantity;
            return ingredient.Kind == IngredientKind.Packaging
                ? new CostBreakdown(0, cost, 0)
                : new CostBreakdown(cost, 0, 0);
        }

        if (line.ComponentProductId is { } componentId)
        {
            var component = Product(componentId);
            if (path.Contains(componentId))
                throw new DomainException($"Состав зациклен: изделие «{component.Name}» входит само в себя через другие изделия.");
            return BatchCost(componentId, path) / component.YieldQuantity * line.Quantity;
        }

        throw new DomainException("В строке состава должен быть указан либо продукт, либо изделие.");
    }

    private void Collect(Guid productId, decimal quantity, Dictionary<Guid, decimal> result, HashSet<Guid> path)
    {
        var product = Product(productId);
        if (!path.Add(productId))
            throw new DomainException($"Состав зациклен: изделие «{product.Name}» входит само в себя через другие изделия.");

        var batches = quantity / product.YieldQuantity;
        foreach (var line in product.Lines)
        {
            var needed = line.Quantity * batches;
            if (line.IngredientId is { } ingredientId)
            {
                Ingredient(ingredientId);
                result[ingredientId] = result.GetValueOrDefault(ingredientId) + needed;
            }
            else if (line.ComponentProductId is { } componentId)
            {
                Collect(componentId, needed, result, path);
            }
        }

        path.Remove(productId);
    }

    private CostBreakdown LaborCost(decimal minutes) => new(0, 0, minutes / 60m * _hourlyRate);

    private Product Product(Guid id) =>
        _products.TryGetValue(id, out var product)
            ? product
            : throw new DomainException("Изделие из состава не найдено. Возможно, оно было удалено.");

    private Ingredient Ingredient(Guid id) =>
        _ingredients.TryGetValue(id, out var ingredient)
            ? ingredient
            : throw new DomainException("Продукт из состава не найден. Возможно, он был удалён.");
}
