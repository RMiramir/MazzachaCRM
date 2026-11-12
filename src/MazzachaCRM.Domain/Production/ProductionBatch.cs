using MazzachaCRM.Domain.Common;
using MazzachaCRM.Domain.Products;

namespace MazzachaCRM.Domain.Production;

/// <summary>Продажа из партии: сколько штук и по какой цене.</summary>
public readonly record struct SaleEntry(decimal Quantity, decimal UnitPrice);

public sealed class BatchSale : Entity
{
    private BatchSale()
    {
    }

    public Guid BatchId { get; private set; }
    public DateOnly Date { get; private set; }
    public decimal Quantity { get; private set; }
    public decimal UnitPrice { get; private set; }

    public decimal Amount => Quantity * UnitPrice;

    internal static BatchSale Create(Guid batchId, DateOnly date, decimal quantity, decimal unitPrice) =>
        new() { BatchId = batchId, Date = date, Quantity = quantity, UnitPrice = unitPrice };

    internal void ChangePrice(decimal unitPrice) => UnitPrice = unitPrice;
}

public sealed class BatchWriteOff : Entity
{
    private BatchWriteOff()
    {
    }

    public Guid BatchId { get; private set; }
    public DateOnly Date { get; private set; }
    public decimal Quantity { get; private set; }
    public string? Reason { get; private set; }

    internal static BatchWriteOff Create(Guid batchId, DateOnly date, decimal quantity, string? reason) =>
        new() { BatchId = batchId, Date = date, Quantity = quantity, Reason = Guard.Optional(reason) };
}

/// <summary>
/// Партия изделия, изготовленная в конкретный день: одна строка таблицы дня.
/// Себестоимость фиксируется в момент изготовления и не меняется, когда дорожает сырьё.
/// Непроданный остаток переносится на следующие дни, пока его не продадут или не спишут.
/// </summary>
public sealed class ProductionBatch : AggregateRoot
{
    private readonly List<BatchSale> _sales = [];
    private readonly List<BatchWriteOff> _writeOffs = [];

    private ProductionBatch()
    {
    }

    public DateOnly ProductionDate { get; private set; }
    public Guid ProductId { get; private set; }
    public decimal Quantity { get; private set; }

    public decimal UnitCostIngredients { get; private set; }
    public decimal UnitCostPackaging { get; private set; }
    public decimal UnitCostLabor { get; private set; }

    /// <summary>Цена продажи за штуку, по которой считается потенциальная выручка.</summary>
    public decimal UnitPrice { get; private set; }

    /// <summary>Заказ, под который изготовлена партия. Такая партия не попадает в обычную продажу.</summary>
    public Guid? OrderId { get; private set; }

    /// <summary>День, когда остаток стал нулевым. Пусто, пока в партии что-то осталось.</summary>
    public DateOnly? ClosedOn { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public IReadOnlyCollection<BatchSale> Sales => _sales.AsReadOnly();
    public IReadOnlyCollection<BatchWriteOff> WriteOffs => _writeOffs.AsReadOnly();

    public CostBreakdown UnitCostBreakdown => new(UnitCostIngredients, UnitCostPackaging, UnitCostLabor);
    public decimal UnitCost => UnitCostIngredients + UnitCostPackaging + UnitCostLabor;
    public decimal TotalCost => Quantity * UnitCost;
    public decimal LaborCost => Quantity * UnitCostLabor;

    public decimal SoldQuantity => _sales.Sum(s => s.Quantity);
    public decimal WrittenOffQuantity => _writeOffs.Sum(w => w.Quantity);
    public decimal RemainingQuantity => Quantity - SoldQuantity - WrittenOffQuantity;
    public decimal ActualRevenue => _sales.Sum(s => s.Amount);

    public static ProductionBatch Create(
        DateOnly productionDate,
        Guid productId,
        decimal quantity,
        CostBreakdown unitCost,
        decimal unitPrice,
        Guid? orderId = null)
    {
        var batch = new ProductionBatch
        {
            ProductionDate = productionDate,
            ProductId = productId,
            Quantity = Guard.Positive(quantity, "Количество"),
            UnitPrice = Guard.NotNegative(unitPrice, "Цена"),
            OrderId = orderId,
            CreatedAt = DateTime.UtcNow,
        };
        batch.UpdateCostSnapshot(unitCost);
        return batch;
    }

    public void ChangeQuantity(decimal quantity)
    {
        Guard.Positive(quantity, "Количество");
        var used = SoldQuantity + WrittenOffQuantity;
        if (quantity < used)
            throw new DomainException($"Нельзя уменьшить количество до {quantity:0.##}: уже продано и списано {used:0.##}.");

        Quantity = quantity;
        RefreshClosedOn();
    }

    /// <summary>Меняет цену партии. Продажи по прежней обычной цене пересчитываются на новую.</summary>
    public void ChangePrice(decimal unitPrice)
    {
        Guard.NotNegative(unitPrice, "Цена");
        foreach (var sale in _sales.Where(s => s.UnitPrice == UnitPrice))
            sale.ChangePrice(unitPrice);
        UnitPrice = unitPrice;
    }

    public void UpdateCostSnapshot(CostBreakdown unitCost)
    {
        UnitCostIngredients = unitCost.Ingredients;
        UnitCostPackaging = unitCost.Packaging;
        UnitCostLabor = unitCost.Labor;
    }

    /// <summary>Заменяет продажи за указанный день. Пустой список убирает продажи этого дня.</summary>
    public void SetSalesForDate(DateOnly date, IEnumerable<SaleEntry> entries)
    {
        EnsureNotBeforeProduction(date);

        var list = entries.Where(e => e.Quantity != 0).ToList();
        foreach (var entry in list)
        {
            Guard.Positive(entry.Quantity, "Продано");
            Guard.NotNegative(entry.UnitPrice, "Цена");
        }

        var otherDays = _sales.Where(s => s.Date != date).Sum(s => s.Quantity);
        var available = Quantity - otherDays - WrittenOffQuantity;
        var requested = list.Sum(e => e.Quantity);
        if (requested > available)
            throw new DomainException($"Нельзя продать {requested:0.##}: в партии доступно только {available:0.##}.");

        _sales.RemoveAll(s => s.Date == date);
        _sales.AddRange(list.Select(e => BatchSale.Create(Id, date, e.Quantity, e.UnitPrice)));
        RefreshClosedOn();
    }

    /// <summary>Заменяет списание за указанный день. Ноль убирает списание.</summary>
    public void SetWriteOffForDate(DateOnly date, decimal quantity, string? reason)
    {
        EnsureNotBeforeProduction(date);
        Guard.NotNegative(quantity, "Списано");

        var otherDays = _writeOffs.Where(w => w.Date != date).Sum(w => w.Quantity);
        var available = Quantity - otherDays - SoldQuantity;
        if (quantity > available)
            throw new DomainException($"Нельзя списать {quantity:0.##}: в партии доступно только {available:0.##}.");

        _writeOffs.RemoveAll(w => w.Date == date);
        if (quantity > 0)
            _writeOffs.Add(BatchWriteOff.Create(Id, date, quantity, reason));
        RefreshClosedOn();
    }

    public void ClearSales()
    {
        _sales.Clear();
        RefreshClosedOn();
    }

    /// <summary>Отвязывает партию от заказа: изделие становится обычным остатком.</summary>
    public void ReleaseFromOrder() => OrderId = null;

    public decimal SoldOn(DateOnly date) => _sales.Where(s => s.Date == date).Sum(s => s.Quantity);

    public decimal RevenueOn(DateOnly date) => _sales.Where(s => s.Date == date).Sum(s => s.Amount);

    public decimal WrittenOffOn(DateOnly date) => _writeOffs.Where(w => w.Date == date).Sum(w => w.Quantity);

    public decimal SoldAfter(DateOnly date) => _sales.Where(s => s.Date > date).Sum(s => s.Quantity);

    public decimal WrittenOffAfter(DateOnly date) => _writeOffs.Where(w => w.Date > date).Sum(w => w.Quantity);

    /// <summary>Остаток на утро указанного дня.</summary>
    public decimal RemainingAtStartOf(DateOnly date) =>
        Quantity - _sales.Where(s => s.Date < date).Sum(s => s.Quantity) - _writeOffs.Where(w => w.Date < date).Sum(w => w.Quantity);

    /// <summary>Остаток на вечер указанного дня.</summary>
    public decimal RemainingAtEndOf(DateOnly date) =>
        Quantity - _sales.Where(s => s.Date <= date).Sum(s => s.Quantity) - _writeOffs.Where(w => w.Date <= date).Sum(w => w.Quantity);

    private void EnsureNotBeforeProduction(DateOnly date)
    {
        if (date < ProductionDate)
            throw new DomainException("Дата не может быть раньше дня изготовления партии.");
    }

    private void RefreshClosedOn()
    {
        if (RemainingQuantity > 0)
        {
            ClosedOn = null;
            return;
        }

        var dates = _sales.Select(s => s.Date).Concat(_writeOffs.Select(w => w.Date)).ToList();
        ClosedOn = dates.Count == 0 ? ProductionDate : dates.Max();
    }
}
