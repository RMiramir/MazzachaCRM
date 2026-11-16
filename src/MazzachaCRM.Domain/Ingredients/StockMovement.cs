using MazzachaCRM.Domain.Common;

namespace MazzachaCRM.Domain.Ingredients;

public enum StockMovementType
{
    Purchase = 1,
    Production = 2,
    Adjustment = 3,
    WriteOff = 4,
}

/// <summary>
/// Запись журнала склада. Количество со знаком: приход положительный, расход отрицательный.
/// Создаётся только методами <see cref="Ingredient"/>, чтобы остаток и журнал не расходились.
/// </summary>
public sealed class StockMovement : AggregateRoot
{
    private StockMovement()
    {
    }

    public Guid IngredientId { get; private set; }
    public DateOnly Date { get; private set; }
    public StockMovementType Type { get; private set; }
    public decimal Quantity { get; private set; }

    /// <summary>Сколько заплатили за закупку. Для остальных типов не заполняется.</summary>
    public decimal? TotalCost { get; private set; }

    /// <summary>Партия, на которую ушло сырьё.</summary>
    public Guid? BatchId { get; private set; }

    public string? Note { get; private set; }
    public DateTime CreatedAt { get; private set; }

    /// <summary>
    /// Отдаёт долю расхода другой партии, когда часть изделий отделяют под заказ.
    /// Остаток на складе не меняется: то же сырьё просто числится за двумя партиями.
    /// </summary>
    public StockMovement SplitForBatch(decimal share, Guid batchId)
    {
        if (share <= 0 || share >= 1)
            throw new DomainException("Доля должна быть больше нуля и меньше единицы.");

        var part = Quantity * share;
        Quantity -= part;
        return Create(IngredientId, Date, Type, part, null, batchId, Note);
    }

    /// <summary>Переносит расход на другую партию, когда изделия возвращают в исходную строку.</summary>
    public void MoveToBatch(Guid batchId) => BatchId = batchId;

    internal static StockMovement Create(
        Guid ingredientId,
        DateOnly date,
        StockMovementType type,
        decimal quantity,
        decimal? totalCost,
        Guid? batchId,
        string? note) =>
        new()
        {
            IngredientId = ingredientId,
            Date = date,
            Type = type,
            Quantity = quantity,
            TotalCost = totalCost,
            BatchId = batchId,
            Note = Guard.Optional(note),
            CreatedAt = DateTime.UtcNow,
        };
}
