using MazzachaCRM.Domain.Common;

namespace MazzachaCRM.Domain.Ingredients;

public enum MeasureUnit
{
    Gram = 1,
    Milliliter = 2,
    Piece = 3,
}

public enum IngredientKind
{
    /// <summary>Сырьё: мука, сахар, яйца.</summary>
    Ingredient = 1,

    /// <summary>Упаковка: коробки, стаканы, формочки.</summary>
    Packaging = 2,
}

/// <summary>
/// Продукт или упаковка. Цена задаётся за упаковку определённого размера
/// (например, 12 сомони за 1000 г), себестоимость считается по цене за единицу.
/// Остаток на складе меняется только через методы, которые создают запись в журнале.
/// </summary>
public sealed class Ingredient : AggregateRoot
{
    private Ingredient()
    {
    }

    public string Name { get; private set; } = string.Empty;
    public IngredientKind Kind { get; private set; }
    public MeasureUnit Unit { get; private set; }
    public decimal PackagePrice { get; private set; }
    public decimal PackageSize { get; private set; }
    public decimal StockQuantity { get; private set; }
    public decimal? MinStock { get; private set; }

    public decimal UnitPrice => PackageSize == 0 ? 0 : PackagePrice / PackageSize;
    public bool IsOutOfStock => StockQuantity < 0;
    public bool IsLowStock => MinStock is { } min && StockQuantity <= min;

    public static Ingredient Create(
        string name,
        IngredientKind kind,
        MeasureUnit unit,
        decimal packagePrice,
        decimal packageSize,
        decimal? minStock = null)
    {
        var ingredient = new Ingredient { Unit = unit };
        ingredient.Update(name, kind, packagePrice, packageSize, minStock);
        return ingredient;
    }

    public void Update(string name, IngredientKind kind, decimal packagePrice, decimal packageSize, decimal? minStock)
    {
        Name = Guard.NotEmpty(name, "Название");
        Kind = kind;
        PackagePrice = Guard.NotNegative(packagePrice, "Цена");
        PackageSize = Guard.Positive(packageSize, "Размер упаковки");
        MinStock = minStock is null ? null : Guard.NotNegative(minStock.Value, "Минимальный остаток");
    }

    /// <summary>Менять единицу можно, только пока продукт нигде не используется: это проверяет приложение.</summary>
    public void ChangeUnit(MeasureUnit unit) => Unit = unit;

    /// <summary>
    /// Закупка: увеличивает остаток и, если нужно, обновляет текущую цену.
    /// Размер упаковки остаётся прежним, цена пересчитывается на него.
    /// </summary>
    public StockMovement RegisterPurchase(DateOnly date, decimal quantity, decimal totalCost, bool updatePrice, string? note)
    {
        Guard.Positive(quantity, "Количество");
        Guard.NotNegative(totalCost, "Сумма закупки");

        StockQuantity += quantity;
        if (updatePrice)
            PackagePrice = totalCost / quantity * PackageSize;

        return StockMovement.Create(Id, date, StockMovementType.Purchase, quantity, totalCost, null, note);
    }

    /// <summary>Списание сырья на изготовление партии.</summary>
    public StockMovement ConsumeForBatch(DateOnly date, decimal quantity, Guid batchId, string? note)
    {
        Guard.Positive(quantity, "Количество");
        StockQuantity -= quantity;
        return StockMovement.Create(Id, date, StockMovementType.Production, -quantity, null, batchId, note);
    }

    /// <summary>Инвентаризация: выставляет фактический остаток и записывает разницу.</summary>
    public StockMovement AdjustStockTo(DateOnly date, decimal actualQuantity, string? note)
    {
        var delta = actualQuantity - StockQuantity;
        if (delta == 0)
            throw new DomainException("Остаток уже равен указанному значению.");

        StockQuantity = actualQuantity;
        return StockMovement.Create(Id, date, StockMovementType.Adjustment, delta, null, null, note);
    }

    /// <summary>Списание испорченного или потерянного сырья.</summary>
    public StockMovement WriteOff(DateOnly date, decimal quantity, string? note)
    {
        Guard.Positive(quantity, "Количество");
        StockQuantity -= quantity;
        return StockMovement.Create(Id, date, StockMovementType.WriteOff, -quantity, null, null, note);
    }

    /// <summary>Отменяет влияние записи журнала на остаток (перед её удалением).</summary>
    public void RevertMovement(StockMovement movement)
    {
        if (movement.IngredientId != Id)
            throw new DomainException("Запись журнала относится к другому продукту.");
        StockQuantity -= movement.Quantity;
    }
}
