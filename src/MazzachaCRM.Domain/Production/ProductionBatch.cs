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
/// Затраты партии относятся только к дню изготовления. Непроданный остаток сам на следующие
/// дни не переходит: его переносят командой, и тогда в этих днях его можно продать или списать.
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

    /// <summary>Позиция заказа, под которую изготовлена партия.</summary>
    public Guid? OrderItemId { get; private set; }

    /// <summary>
    /// Изделия не пекли под заказ, а взяли из уже готовых. При возврате заказа в работу
    /// такая партия остаётся закреплённой, а не удаляется.
    /// </summary>
    public bool ReservedFromStock { get; private set; }

    /// <summary>Свободная партия, от которой отделили эти изделия для заказа.</summary>
    public Guid? SplitFromBatchId { get; private set; }

    /// <summary>Цена витрины до закрепления за заказом: возвращается, если заказ отменят.</summary>
    public decimal? PriceBeforeOrder { get; private set; }

    /// <summary>День, когда остаток стал нулевым. Пусто, пока в партии что-то осталось.</summary>
    public DateOnly? ClosedOn { get; private set; }

    /// <summary>
    /// До какого дня включительно перенесён остаток. Пусто — остаток не переносили,
    /// он остаётся в дне изготовления.
    /// </summary>
    public DateOnly? CarriedUntil { get; private set; }

    /// <summary>Последний день, в который партию можно продавать и списывать.</summary>
    public DateOnly LastAvailableDay => CarriedUntil ?? ProductionDate;

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
        Guid? orderId = null,
        Guid? orderItemId = null)
    {
        var batch = new ProductionBatch
        {
            ProductionDate = productionDate,
            ProductId = productId,
            Quantity = Guard.Positive(quantity, "Количество"),
            UnitPrice = Guard.NotNegative(unitPrice, "Цена"),
            OrderId = orderId,
            OrderItemId = orderItemId,
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
    public void ReleaseFromOrder()
    {
        OrderId = null;
        OrderItemId = null;

        // Взятое из готового возвращается на витрину по прежней цене.
        if (ReservedFromStock)
            UnitPrice = PriceBeforeOrder ?? UnitPrice;
        ReservedFromStock = false;
        PriceBeforeOrder = null;
    }

    /// <summary>
    /// Закрепляет готовые изделия за позицией заказа. Если берётся вся партия, она целиком
    /// переходит к заказу; иначе от неё отделяется новая партия с той же датой и себестоимостью,
    /// а здесь остаётся свободный остаток. Возвращает партию, которая теперь принадлежит заказу.
    /// </summary>
    public ProductionBatch ReserveForOrder(decimal quantity, Guid orderId, Guid orderItemId, decimal unitPrice)
    {
        if (OrderId is not null)
            throw new DomainException("Эти изделия уже закреплены за заказом.");
        Guard.Positive(quantity, "Количество");
        Guard.NotNegative(unitPrice, "Цена");
        if (quantity > RemainingQuantity)
            throw new DomainException($"Свободно только {RemainingQuantity:0.###} шт, а в заказе указано {quantity:0.###}.");

        if (quantity == Quantity)
        {
            PriceBeforeOrder = UnitPrice;
            UnitPrice = unitPrice;
            OrderId = orderId;
            OrderItemId = orderItemId;
            ReservedFromStock = true;
            return this;
        }

        Quantity -= quantity;
        RefreshClosedOn();

        return new ProductionBatch
        {
            ProductionDate = ProductionDate,
            ProductId = ProductId,
            Quantity = quantity,
            UnitCostIngredients = UnitCostIngredients,
            UnitCostPackaging = UnitCostPackaging,
            UnitCostLabor = UnitCostLabor,
            UnitPrice = unitPrice,
            OrderId = orderId,
            OrderItemId = orderItemId,
            ReservedFromStock = true,
            SplitFromBatchId = SplitFromBatchId ?? Id,
            PriceBeforeOrder = UnitPrice,
            CarriedUntil = CarriedUntil,
            CreatedAt = DateTime.UtcNow,
        };
    }

    /// <summary>Можно ли продавать и списывать из партии в этот день.</summary>
    public bool IsAvailableOn(DateOnly date) => date >= ProductionDate && date <= LastAvailableDay;

    public void EnsureAvailableOn(DateOnly date)
    {
        if (date < ProductionDate)
            throw new DomainException("Дата не может быть раньше дня изготовления партии.");
        if (date > LastAvailableDay)
            throw new DomainException(
                $"Остаток от {ProductionDate:dd.MM} не перенесён на {date:dd.MM}. Сначала перенесите его на этот день.");
    }

    /// <summary>Переносит непроданный остаток вперёд, по указанный день включительно.</summary>
    public void CarryTo(DateOnly date)
    {
        if (date <= ProductionDate)
            throw new DomainException("Перенести остаток можно только на следующие дни.");
        if (RemainingQuantity <= 0)
            throw new DomainException("Переносить нечего: всё продано или списано.");
        if (date > LastAvailableDay)
            CarriedUntil = date;
    }

    /// <summary>
    /// Отменяет перенос после указанного дня: остаток снова заканчивается в этот день.
    /// Нельзя, если в следующих днях из партии уже продавали или списывали.
    /// </summary>
    public void CancelCarryAfter(DateOnly lastDay)
    {
        if (lastDay < ProductionDate)
            throw new DomainException("Дата не может быть раньше дня изготовления партии.");
        if (lastDay >= LastAvailableDay)
            return;
        if (_sales.Any(s => s.Date > lastDay) || _writeOffs.Any(w => w.Date > lastDay))
            throw new DomainException(
                $"Перенесённое уже продавали или списывали после {lastDay:dd.MM}. Сначала уберите эти продажи и списания.");

        CarriedUntil = lastDay == ProductionDate ? null : lastDay;
    }

    /// <summary>Можно ли вернуть отделённые изделия обратно в эту партию одной строкой.</summary>
    public bool CanAbsorb(ProductionBatch part) =>
        OrderId is null &&
        part.OrderId is null &&
        part.SplitFromBatchId == Id &&
        part.ProductionDate == ProductionDate &&
        part.UnitCostBreakdown == UnitCostBreakdown &&
        part._sales.Count == 0 &&
        part._writeOffs.Count == 0;

    /// <summary>Возвращает в партию изделия, которые отделяли для заказа.</summary>
    public void Absorb(ProductionBatch part)
    {
        if (!CanAbsorb(part))
            throw new DomainException("Эти изделия нельзя вернуть в исходную строку.");
        Quantity += part.Quantity;
        RefreshClosedOn();
    }

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
