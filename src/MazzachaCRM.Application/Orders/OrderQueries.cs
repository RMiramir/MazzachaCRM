using MazzachaCRM.Application.Abstractions;
using MazzachaCRM.Application.Common;
using MazzachaCRM.Domain.Common;
using MazzachaCRM.Domain.Orders;
using MazzachaCRM.Domain.Production;
using MazzachaCRM.Domain.Products;
using Microsoft.EntityFrameworkCore;

namespace MazzachaCRM.Application.Orders;

public sealed record OrderItemDto(
    Guid Id,
    Guid ProductId,
    string ProductName,
    decimal Quantity,
    decimal Returned,
    decimal ReturnedWrittenOff,
    decimal Sold,
    decimal UnitPrice,
    decimal Amount,
    string? Note);

public sealed record OrderPaymentDto(Guid Id, DateOnly Date, decimal Amount, string? Note);

public sealed record OrderDto(
    Guid Id,
    int Number,
    Guid CustomerId,
    string CustomerName,
    string? CustomerPhone,
    bool IsConsignment,
    DateOnly DueDate,
    TimeOnly? DueTime,
    OrderStatus Status,
    decimal Total,
    decimal Paid,
    decimal Balance,
    bool IsPaid,
    string? Note,
    DateOnly? ProducedDate,
    DateOnly? DeliveredDate,
    DateOnly? ReportedDate,
    IReadOnlyList<OrderItemDto> Items,
    IReadOnlyList<OrderPaymentDto> Payments,
    string ItemsSummary,
    decimal? Cost,
    decimal? Profit);

public enum OrderScope
{
    /// <summary>Ещё не выданные.</summary>
    Active = 1,

    /// <summary>Изготовлены или выданы, но оплачены не полностью.</summary>
    Unpaid = 2,

    All = 3,
}

public sealed record GetOrdersQuery(OrderScope Scope, DateOnly? From, DateOnly? To, Guid? CustomerId)
    : IQuery<IReadOnlyList<OrderDto>>;

public sealed class GetOrdersHandler(IReadDbContext db, CostingContextLoader costing)
    : IQueryHandler<GetOrdersQuery, IReadOnlyList<OrderDto>>
{
    public async Task<IReadOnlyList<OrderDto>> Handle(GetOrdersQuery query, CancellationToken ct)
    {
        var orders = db.Orders.Include(o => o.Items).Include(o => o.Payments).AsQueryable();
        if (query.Scope == OrderScope.Active)
            orders = orders.Where(o => o.Status != OrderStatus.Delivered && o.Status != OrderStatus.Cancelled);
        if (query.Scope == OrderScope.Unpaid)
            orders = orders.Where(o => o.Status == OrderStatus.Ready || o.Status == OrderStatus.Delivered);
        if (query.From is { } from)
            orders = orders.Where(o => o.DueDate >= from);
        if (query.To is { } to)
            orders = orders.Where(o => o.DueDate <= to);
        if (query.CustomerId is { } customerId)
            orders = orders.Where(o => o.CustomerId == customerId);

        var list = await orders.ToListAsync(ct);
        if (query.Scope == OrderScope.Unpaid)
            list = list.Where(o => o.Balance > 0).ToList();

        var mapper = await OrderMapper.CreateAsync(db, costing, list, ct);

        // Сначала активные по сроку выдачи, затем остальные от новых к старым.
        var active = list.Where(IsActive).OrderBy(o => o.DueDate).ThenBy(o => o.DueTime ?? TimeOnly.MaxValue);
        var finished = list.Where(o => !IsActive(o)).OrderByDescending(o => o.DueDate).ThenByDescending(o => o.Number);
        return active.Concat(finished).Select(mapper.Map).ToList();
    }

    private static bool IsActive(Order order) =>
        order.Status is not (OrderStatus.Delivered or OrderStatus.Cancelled);
}

public sealed record GetOrderQuery(Guid Id) : IQuery<OrderDto>;

public sealed class GetOrderHandler(IReadDbContext db, CostingContextLoader costing)
    : IQueryHandler<GetOrderQuery, OrderDto>
{
    public async Task<OrderDto> Handle(GetOrderQuery query, CancellationToken ct)
    {
        var order = await db.Orders.Include(o => o.Items).Include(o => o.Payments)
                .FirstOrDefaultAsync(o => o.Id == query.Id, ct)
            ?? throw new NotFoundException("Заказ не найден.");

        var mapper = await OrderMapper.CreateAsync(db, costing, [order], ct);
        return mapper.Map(order);
    }
}

/// <summary>Собирает DTO заказа: имена, себестоимость (по партиям, если изготовлен, иначе по текущему составу).</summary>
internal sealed class OrderMapper
{
    private readonly CostingContext _context;
    private readonly Dictionary<Guid, (string Name, string? Phone, bool IsConsignment)> _customers;
    private readonly ILookup<Guid, ProductionBatch> _batchesByOrder;

    private OrderMapper(
        CostingContext context,
        Dictionary<Guid, (string Name, string? Phone, bool IsConsignment)> customers,
        ILookup<Guid, ProductionBatch> batchesByOrder)
    {
        _context = context;
        _customers = customers;
        _batchesByOrder = batchesByOrder;
    }

    public static async Task<OrderMapper> CreateAsync(
        IReadDbContext db, CostingContextLoader costing, IReadOnlyCollection<Order> orders, CancellationToken ct)
    {
        var context = await costing.LoadReadOnlyAsync(ct);

        var customerIds = orders.Select(o => o.CustomerId).Distinct().ToList();
        var customers = (await db.Customers.Where(c => customerIds.Contains(c.Id)).ToListAsync(ct))
            .ToDictionary(c => c.Id, c => (c.Name, c.Phone, c.IsConsignment));

        var orderIds = orders.Select(o => o.Id).ToList();
        var batches = await db.Batches
            .Include(b => b.WriteOffs)
            .Where(b => b.OrderId != null && orderIds.Contains(b.OrderId.Value))
            .ToListAsync(ct);

        return new OrderMapper(context, customers, batches.ToLookup(b => b.OrderId!.Value));
    }

    public OrderDto Map(Order order)
    {
        _customers.TryGetValue(order.CustomerId, out var customer);

        var items = order.Items
            .Select(i => new OrderItemDto(
                i.Id,
                i.ProductId,
                ProductName(i.ProductId),
                i.Quantity,
                i.ReturnedQuantity,
                ReturnedWrittenOff(order, i),
                i.SoldQuantity,
                i.UnitPrice.Precise(),
                i.Amount.Money(),
                i.Note))
            .ToList();

        var payments = order.Payments
            .OrderBy(p => p.Date)
            .Select(p => new OrderPaymentDto(p.Id, p.Date, p.Amount.Money(), p.Note))
            .ToList();

        var cost = Cost(order);
        return new OrderDto(
            order.Id,
            order.Number,
            order.CustomerId,
            customer.Name ?? "—",
            customer.Phone,
            customer.IsConsignment,
            order.DueDate,
            order.DueTime,
            order.Status,
            order.Total.Money(),
            order.Paid.Money(),
            order.Balance.Money(),
            order.IsPaid,
            order.Note,
            order.ProducedDate,
            order.DeliveredDate,
            order.ReportedDate,
            items,
            payments,
            string.Join(", ", items.Select(i => $"{i.ProductName} × {i.Quantity:0.###}")),
            cost?.Money(),
            cost is { } value ? (order.Total - value).Money() : null);
    }

    /// <summary>Сколько возврата списано сразу, в день отправки.</summary>
    private decimal ReturnedWrittenOff(Order order, OrderItem item)
    {
        var batch = _batchesByOrder[order.Id].FirstOrDefault(b => b.OrderItemId == item.Id);
        return batch?.WrittenOffOn(batch.ProductionDate) ?? 0;
    }

    private decimal? Cost(Order order)
    {
        var batches = _batchesByOrder[order.Id].ToList();
        if (batches.Count > 0)
            return batches.Sum(b => b.TotalCost);

        try
        {
            return order.Items.Sum(i => _context.Calculator.UnitCost(i.ProductId).Total * i.Quantity);
        }
        catch (DomainException)
        {
            return null;
        }
    }

    private string ProductName(Guid productId) =>
        _context.Products.TryGetValue(productId, out Product? product) ? product.Name : "Удалённое изделие";
}
