using MazzachaCRM.Domain.Common;

namespace MazzachaCRM.Domain.Orders;

public enum OrderStatus
{
    New = 1,
    InProgress = 2,
    Ready = 3,

    /// <summary>Выдан клиенту или отправлен в магазин.</summary>
    Delivered = 4,

    Cancelled = 5,
}

public readonly record struct OrderItemSpec(Guid ProductId, decimal Quantity, decimal UnitPrice, string? Note);

public sealed class OrderItem : Entity
{
    private OrderItem()
    {
    }

    public Guid OrderId { get; private set; }
    public Guid ProductId { get; private set; }
    public decimal Quantity { get; private set; }
    public decimal UnitPrice { get; private set; }
    public string? Note { get; private set; }

    /// <summary>Сколько вернул магазин по отчёту. За возвраты клиент не платит.</summary>
    public decimal ReturnedQuantity { get; private set; }

    public decimal SoldQuantity => Quantity - ReturnedQuantity;
    public decimal Amount => SoldQuantity * UnitPrice;

    internal static OrderItem Create(Guid orderId, OrderItemSpec spec) =>
        new()
        {
            OrderId = orderId,
            ProductId = spec.ProductId,
            Quantity = spec.Quantity,
            UnitPrice = spec.UnitPrice,
            Note = Guard.Optional(spec.Note),
        };

    internal void SetReturned(decimal returned)
    {
        if (returned < 0 || returned > Quantity)
            throw new DomainException($"Возврат должен быть от 0 до {Quantity:0.###}.");
        ReturnedQuantity = returned;
    }
}

/// <summary>Платёж по заказу: предоплата, доплата или деньги от магазина после продажи.</summary>
public sealed class OrderPayment : Entity
{
    private OrderPayment()
    {
    }

    public Guid OrderId { get; private set; }
    public DateOnly Date { get; private set; }
    public decimal Amount { get; private set; }
    public string? Note { get; private set; }

    internal static OrderPayment Create(Guid orderId, DateOnly date, decimal amount, string? note) =>
        new() { OrderId = orderId, Date = date, Amount = amount, Note = Guard.Optional(note) };
}

/// <summary>
/// Заказ клиента или поставка в магазин. Когда заказ готов, под него создаются партии,
/// и его затраты и выручка записываются в день готовности. Деньги учитываются отдельно —
/// платежами в те дни, когда они пришли. Разница между суммой заказа и платежами — долг.
/// </summary>
public sealed class Order : AggregateRoot
{
    private readonly List<OrderItem> _items = [];
    private readonly List<OrderPayment> _payments = [];

    private Order()
    {
    }

    public int Number { get; private set; }
    public Guid CustomerId { get; private set; }
    public DateOnly DueDate { get; private set; }
    public TimeOnly? DueTime { get; private set; }
    public string? Note { get; private set; }
    public OrderStatus Status { get; private set; }
    public DateTime CreatedAt { get; private set; }

    /// <summary>День готовности: в него записываются затраты и выручка заказа.</summary>
    public DateOnly? ProducedDate { get; private set; }

    public DateOnly? DeliveredDate { get; private set; }

    /// <summary>Когда магазин сообщил, сколько продано и сколько вернули.</summary>
    public DateOnly? ReportedDate { get; private set; }

    public IReadOnlyCollection<OrderItem> Items => _items.AsReadOnly();
    public IReadOnlyCollection<OrderPayment> Payments => _payments.AsReadOnly();

    public decimal Total => _items.Sum(i => i.Amount);
    public decimal Paid => _payments.Sum(p => p.Amount);
    public decimal Balance => Total - Paid;

    /// <summary>Заказ изготовлен: под него существуют партии.</summary>
    public bool IsProduced => Status is OrderStatus.Ready or OrderStatus.Delivered;

    /// <summary>Выдан и полностью оплачен.</summary>
    public bool IsPaid => Status == OrderStatus.Delivered && Balance <= 0;

    public static Order Create(
        int number,
        Guid customerId,
        DateOnly dueDate,
        TimeOnly? dueTime,
        string? note,
        IEnumerable<OrderItemSpec> items)
    {
        var order = new Order
        {
            Number = number,
            Status = OrderStatus.New,
            CreatedAt = DateTime.UtcNow,
        };
        order.UpdateDetails(customerId, dueDate, dueTime, note);
        order.SetItems(items);
        return order;
    }

    public void UpdateDetails(Guid customerId, DateOnly dueDate, TimeOnly? dueTime, string? note)
    {
        CustomerId = customerId;
        DueDate = dueDate;
        DueTime = dueTime;
        Note = Guard.Optional(note);
    }

    public void SetItems(IEnumerable<OrderItemSpec> items)
    {
        if (IsProduced)
            throw new DomainException("Заказ уже изготовлен. Чтобы изменить позиции, верните его в статус «Новый» или «Готовится».");
        if (Status == OrderStatus.Cancelled)
            throw new DomainException("Заказ отменён. Чтобы изменить позиции, сначала верните его в работу.");

        var specs = items.ToList();
        if (specs.Count == 0)
            throw new DomainException("Добавьте в заказ хотя бы одно изделие.");

        foreach (var spec in specs)
        {
            Guard.Positive(spec.Quantity, "Количество");
            Guard.NotNegative(spec.UnitPrice, "Цена");
        }

        _items.Clear();
        _items.AddRange(specs.Select(spec => OrderItem.Create(Id, spec)));
    }

    public bool HasSameItems(IEnumerable<OrderItemSpec> items)
    {
        var current = _items.Select(i => (i.ProductId, i.Quantity, i.UnitPrice, i.Note)).ToList();
        var incoming = items.Select(i => (i.ProductId, i.Quantity, i.UnitPrice, Guard.Optional(i.Note))).ToList();
        return current.SequenceEqual(incoming);
    }

    public OrderPayment AddPayment(DateOnly date, decimal amount, string? note)
    {
        Guard.Positive(amount, "Сумма оплаты");
        var payment = OrderPayment.Create(Id, date, amount, note);
        _payments.Add(payment);
        return payment;
    }

    public void RemovePayment(Guid paymentId)
    {
        var payment = _payments.FirstOrDefault(p => p.Id == paymentId)
            ?? throw new DomainException("Оплата не найдена.");
        _payments.Remove(payment);
    }

    /// <summary>
    /// Отчёт магазина: сколько вернули по каждой позиции. Возвращать можно только после изготовления.
    /// </summary>
    public void RecordReturns(DateOnly date, IReadOnlyDictionary<Guid, decimal> returnedByItem)
    {
        if (!IsProduced)
            throw new DomainException("Отчёт магазина вносится после того, как поставка изготовлена и отправлена.");
        if (ProducedDate is { } produced && date < produced)
            throw new DomainException("Дата отчёта не может быть раньше дня отправки.");

        foreach (var (itemId, returned) in returnedByItem)
        {
            var item = _items.FirstOrDefault(i => i.Id == itemId)
                ?? throw new DomainException("Позиция заказа не найдена.");
            item.SetReturned(returned);
        }

        ReportedDate = date;
    }

    public void ChangeStatus(OrderStatus status, DateOnly date)
    {
        if (status == Status)
            return;
        if (!CanTransition(Status, status))
            throw new DomainException($"Нельзя перевести заказ из статуса «{Title(Status)}» в «{Title(status)}».");

        Status = status;
        switch (status)
        {
            case OrderStatus.Ready:
                ProducedDate ??= date;
                DeliveredDate = null;
                break;
            case OrderStatus.Delivered:
                ProducedDate ??= date;
                DeliveredDate = date;
                break;
            default:
                // Изготовленное отменено или возвращено в работу: возвраты по отчёту больше не действуют.
                ProducedDate = null;
                DeliveredDate = null;
                ReportedDate = null;
                foreach (var item in _items)
                    item.SetReturned(0);
                break;
        }
    }

    public static bool CanTransition(OrderStatus from, OrderStatus to) =>
        from switch
        {
            OrderStatus.New or OrderStatus.InProgress or OrderStatus.Ready => to != from,
            OrderStatus.Delivered => to == OrderStatus.Ready,
            OrderStatus.Cancelled => to == OrderStatus.New,
            _ => false,
        };

    public static string Title(OrderStatus status) =>
        status switch
        {
            OrderStatus.New => "Новый",
            OrderStatus.InProgress => "Готовится",
            OrderStatus.Ready => "Готов",
            OrderStatus.Delivered => "Выдан",
            OrderStatus.Cancelled => "Отменён",
            _ => status.ToString(),
        };
}

public interface IOrderRepository
{
    /// <summary>Заказ вместе с позициями и оплатами.</summary>
    Task<Order?> GetByIdAsync(Guid id, CancellationToken ct);

    Task<int> NextNumberAsync(CancellationToken ct);
    Task<bool> AnyForCustomerAsync(Guid customerId, CancellationToken ct);
    Task<bool> AnyForProductAsync(Guid productId, CancellationToken ct);
    void Add(Order order);
    void Remove(Order order);
}
