using MazzachaCRM.Domain.Common;

namespace MazzachaCRM.Domain.Orders;

public enum OrderStatus
{
    New = 1,
    InProgress = 2,
    Ready = 3,
    Completed = 4,
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

    public decimal Amount => Quantity * UnitPrice;

    internal static OrderItem Create(Guid orderId, OrderItemSpec spec) =>
        new()
        {
            OrderId = orderId,
            ProductId = spec.ProductId,
            Quantity = spec.Quantity,
            UnitPrice = spec.UnitPrice,
            Note = Guard.Optional(spec.Note),
        };
}

/// <summary>
/// Предзаказ клиента. Когда заказ изготовлен («Готов»), под него создаются партии;
/// когда выдан, по ним записывается продажа, и она попадает в выручку дня выдачи.
/// </summary>
public sealed class Order : AggregateRoot
{
    private readonly List<OrderItem> _items = [];

    private Order()
    {
    }

    public int Number { get; private set; }
    public Guid CustomerId { get; private set; }
    public DateOnly DueDate { get; private set; }
    public TimeOnly? DueTime { get; private set; }
    public decimal Prepayment { get; private set; }
    public string? Note { get; private set; }
    public OrderStatus Status { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateOnly? ProducedDate { get; private set; }
    public DateOnly? CompletedDate { get; private set; }

    public IReadOnlyCollection<OrderItem> Items => _items.AsReadOnly();

    public decimal Total => _items.Sum(i => i.Amount);
    public decimal Balance => Total - Prepayment;

    /// <summary>Заказ изготовлен: под него существуют партии.</summary>
    public bool IsProduced => Status is OrderStatus.Ready or OrderStatus.Completed;

    public static Order Create(
        int number,
        Guid customerId,
        DateOnly dueDate,
        TimeOnly? dueTime,
        decimal prepayment,
        string? note,
        IEnumerable<OrderItemSpec> items)
    {
        var order = new Order
        {
            Number = number,
            Status = OrderStatus.New,
            CreatedAt = DateTime.UtcNow,
        };
        order.UpdateDetails(customerId, dueDate, dueTime, prepayment, note);
        order.SetItems(items);
        return order;
    }

    public void UpdateDetails(Guid customerId, DateOnly dueDate, TimeOnly? dueTime, decimal prepayment, string? note)
    {
        CustomerId = customerId;
        DueDate = dueDate;
        DueTime = dueTime;
        Prepayment = Guard.NotNegative(prepayment, "Предоплата");
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
                CompletedDate = null;
                break;
            case OrderStatus.Completed:
                ProducedDate ??= date;
                CompletedDate = date;
                break;
            default:
                ProducedDate = null;
                CompletedDate = null;
                break;
        }
    }

    public static bool CanTransition(OrderStatus from, OrderStatus to) =>
        from switch
        {
            OrderStatus.New or OrderStatus.InProgress or OrderStatus.Ready => to != from,
            OrderStatus.Completed => to == OrderStatus.Ready,
            OrderStatus.Cancelled => to == OrderStatus.New,
            _ => false,
        };

    public static string Title(OrderStatus status) =>
        status switch
        {
            OrderStatus.New => "Новый",
            OrderStatus.InProgress => "Готовится",
            OrderStatus.Ready => "Готов",
            OrderStatus.Completed => "Выдан",
            OrderStatus.Cancelled => "Отменён",
            _ => status.ToString(),
        };
}

public interface IOrderRepository
{
    /// <summary>Заказ вместе с позициями.</summary>
    Task<Order?> GetByIdAsync(Guid id, CancellationToken ct);

    Task<int> NextNumberAsync(CancellationToken ct);
    Task<bool> AnyForCustomerAsync(Guid customerId, CancellationToken ct);
    Task<bool> AnyForProductAsync(Guid productId, CancellationToken ct);
    void Add(Order order);
    void Remove(Order order);
}
