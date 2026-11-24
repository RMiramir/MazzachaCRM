using MazzachaCRM.Domain.Common;
using MazzachaCRM.Domain.Ingredients;

namespace MazzachaCRM.Domain.Products;

public enum ProductType
{
    /// <summary>Готовое изделие: продаётся поштучно.</summary>
    Finished = 1,

    /// <summary>Полуфабрикат (крем, коржи): не продаётся, входит в состав других изделий.</summary>
    SemiFinished = 2,
}

public enum CostMode
{
    /// <summary>Себестоимость считается по составу.</summary>
    Recipe = 1,

    /// <summary>Себестоимость за единицу указана вручную, состава нет.</summary>
    Manual = 2,
}

/// <summary>Строка состава до сохранения: либо продукт, либо другое изделие.</summary>
public readonly record struct RecipeLineSpec(Guid? IngredientId, Guid? ComponentProductId, decimal Quantity);

public sealed class RecipeLine : Entity
{
    private RecipeLine()
    {
    }

    public Guid ProductId { get; private set; }
    public Guid? IngredientId { get; private set; }
    public Guid? ComponentProductId { get; private set; }
    public decimal Quantity { get; private set; }

    internal static RecipeLine Create(Guid productId, RecipeLineSpec spec) =>
        new()
        {
            ProductId = productId,
            IngredientId = spec.IngredientId,
            ComponentProductId = spec.ComponentProductId,
            Quantity = spec.Quantity,
        };
}

/// <summary>
/// Изделие или полуфабрикат с составом на одну партию.
/// Себестоимость единицы = стоимость партии / выход.
/// </summary>
public sealed class Product : AggregateRoot
{
    private readonly List<RecipeLine> _lines = [];

    private Product()
    {
    }

    public string Name { get; private set; } = string.Empty;
    public ProductType Type { get; private set; }

    /// <summary>Сколько единиц получается из одной партии.</summary>
    public decimal YieldQuantity { get; private set; }

    /// <summary>Единица выхода. У готового изделия всегда штуки.</summary>
    public MeasureUnit YieldUnit { get; private set; }

    /// <summary>Минуты работы на одну партию.</summary>
    public decimal LaborMinutes { get; private set; }

    /// <summary>Обычная цена продажи за штуку. Подставляется в партию и в заказ.</summary>
    public decimal SalePrice { get; private set; }

    public string? Note { get; private set; }

    public CostMode CostMode { get; private set; } = CostMode.Recipe;

    /// <summary>Себестоимость одной единицы, если она указана вручную.</summary>
    public decimal ManualUnitCost { get; private set; }

    public bool IsArchived { get; private set; }

    public IReadOnlyCollection<RecipeLine> Lines => _lines.AsReadOnly();

    public static Product Create(
        string name,
        ProductType type,
        decimal yieldQuantity,
        MeasureUnit yieldUnit,
        decimal laborMinutes,
        decimal salePrice,
        string? note,
        IEnumerable<RecipeLineSpec> lines,
        CostMode costMode = CostMode.Recipe,
        decimal manualUnitCost = 0)
    {
        var product = new Product();
        product.Update(name, type, yieldQuantity, yieldUnit, laborMinutes, salePrice, note, lines, costMode, manualUnitCost);
        return product;
    }

    public void Update(
        string name,
        ProductType type,
        decimal yieldQuantity,
        MeasureUnit yieldUnit,
        decimal laborMinutes,
        decimal salePrice,
        string? note,
        IEnumerable<RecipeLineSpec> lines,
        CostMode costMode = CostMode.Recipe,
        decimal manualUnitCost = 0)
    {
        Name = Guard.NotEmpty(name, "Название");
        Type = type;
        YieldQuantity = Guard.Positive(yieldQuantity, "Выход партии");
        YieldUnit = type == ProductType.Finished ? MeasureUnit.Piece : yieldUnit;
        LaborMinutes = Guard.NotNegative(laborMinutes, "Время работы");
        SalePrice = type == ProductType.Finished ? Guard.NotNegative(salePrice, "Цена продажи") : 0;
        Note = Guard.Optional(note);
        CostMode = costMode;

        if (costMode == CostMode.Manual)
        {
            // Вручную указанная себестоимость уже включает всё: состав и работа не нужны.
            ManualUnitCost = Guard.NotNegative(manualUnitCost, "Себестоимость");
            LaborMinutes = 0;
            _lines.Clear();
            return;
        }

        ManualUnitCost = 0;
        SetLines(lines);
    }

    public void Archive() => IsArchived = true;

    public void Restore() => IsArchived = false;

    private void SetLines(IEnumerable<RecipeLineSpec> lines)
    {
        var specs = lines.ToList();
        if (specs.Count == 0)
            throw new DomainException("Добавьте в состав хотя бы один продукт.");

        var seen = new HashSet<(Guid?, Guid?)>();
        foreach (var spec in specs)
        {
            if ((spec.IngredientId is null) == (spec.ComponentProductId is null))
                throw new DomainException("В строке состава должен быть указан либо продукт, либо изделие.");
            if (spec.ComponentProductId == Id)
                throw new DomainException("Изделие не может входить в собственный состав.");
            if (spec.Quantity <= 0)
                throw new DomainException("Количество в составе должно быть больше нуля.");
            if (!seen.Add((spec.IngredientId, spec.ComponentProductId)))
                throw new DomainException("Один и тот же компонент добавлен в состав дважды.");
        }

        _lines.Clear();
        _lines.AddRange(specs.Select(spec => RecipeLine.Create(Id, spec)));
    }
}
