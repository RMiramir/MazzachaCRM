using MazzachaCRM.Application.Abstractions;
using MazzachaCRM.Application.Common;
using MazzachaCRM.Domain.Common;
using MazzachaCRM.Domain.Orders;
using MazzachaCRM.Domain.Production;
using MazzachaCRM.Domain.Products;
using Microsoft.EntityFrameworkCore;

namespace MazzachaCRM.Application.Orders;

public sealed record OrderItemDto(
    Guid ProductId,
    string ProductName,
    decimal Quantity,
    decimal UnitPrice,
    decimal Amount,
    string? Note);

public sealed record OrderDto(
    Guid Id,
    int Number,
    Guid CustomerId,
    string CustomerName,
    string? CustomerPhone,
    DateOnly DueDate,
    TimeOnly? DueTime,
    OrderStatus Status,
    decimal Total,
    decimal Prepayment,
    decimal Balance,
    string? Note,
    DateOnly? ProducedDate,
    DateOnly? CompletedDate,
    IReadOnlyList<OrderItemDto> Items,
    string ItemsSummary,
    decimal? Cost,
    decimal? Profit);

public sealed record GetOrdersQuery(bool ActiveOnly, DateOnly? From, DateOnly? To, Guid? CustomerId)
    : IQuery<IReadOnlyList<OrderDto>>;

public sealed class GetOrdersHandler(IReadDbContext db, CostingContextLoader costing)
    : IQueryHandler<GetOrdersQuery, IReadOnlyList<OrderDto>>
{
    public async Task<IReadOnlyList<OrderDto>> Handle(GetOrdersQuery query, CancellationToken ct)
    {
        var orders = db.Orders.Include(o => o.Items).AsQueryable();
        if (query.ActiveOnly)
            orders = orders.Where(o => o.Status != OrderStatus.Completed && o.Status != OrderStatus.Cancelled);
        if (query.From is { } from)
            orders = orders.Where(o => o.DueDate >= from);
        if (query.To is { } to)
            orders = orders.Where(o => o.DueDate <= to);
        if (query.CustomerId is { } customerId)
            orders = orders.Where(o => o.CustomerId == customerId);

        var list = await orders.ToListAsync(ct);
        var mapper = await OrderMapper.CreateAsync(db, costing, list, ct);

        // Сначала активные по сроку выдачи, затем завершённые от новых к старым.
        var active = list.Where(IsActive).OrderBy(o => o.DueDate).ThenBy(o => o.DueTime ?? TimeOnly.MaxValue);
        var finished = list.Where(o => !IsActive(o)).OrderByDescending(o => o.DueDate).ThenByDescending(o => o.Number);
        return active.Concat(finished).Select(mapper.Map).ToList();
    }

    private static bool IsActive(Order order) =>
        order.Status is not (OrderStatus.Completed or OrderStatus.Cancelled);
}

public sealed record GetOrderQuery(Guid Id) : IQuery<OrderDto>;

public sealed class GetOrderHandler(IReadDbContext db, CostingContextLoader costing)
    : IQueryHandler<GetOrderQuery, OrderDto>
{
    public async Task<OrderDto> Handle(GetOrderQuery query, CancellationToken ct)
    {
        var order = await db.Orders.Include(o => o.Items).FirstOrDefaultAsync(o => o.Id == query.Id, ct)
            ?? throw new NotFoundException("Заказ не найден.");

        var mapper = await OrderMapper.CreateAsync(db, costing, [order], ct);
        return mapper.Map(order);
    }
}

/// <summary>Собирает DTO заказа: имена, себестоимость (по партиям, если изготовлен, иначе по текущему составу).</summary>
internal sealed class OrderMapper
{
    private readonly CostingContext _context;
    private readonly Dictionary<Guid, (string Name, string? Phone)> _customers;
    private readonly ILookup<Guid, ProductionBatch> _batchesByOrder;

    private OrderMapper(
        CostingContext context,
        Dictionary<Guid, (string Name, string? Phone)> customers,
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
            .ToDictionary(c => c.Id, c => (c.Name, c.Phone));

        var orderIds = orders.Select(o => o.Id).ToList();
        var batches = await db.Batches
            .Where(b => b.OrderId != null && orderIds.Contains(b.OrderId.Value))
            .ToListAsync(ct);

        return new OrderMapper(context, customers, batches.ToLookup(b => b.OrderId!.Value));
    }

    public OrderDto Map(Order order)
    {
        _customers.TryGetValue(order.CustomerId, out var customer);

        var items = order.Items
            .Select(i => new OrderItemDto(
                i.ProductId, ProductName(i.ProductId), i.Quantity, i.UnitPrice.Precise(), i.Amount.Money(), i.Note))
            .ToList();

        var cost = Cost(order);
        return new OrderDto(
            order.Id,
            order.Number,
            order.CustomerId,
            customer.Name ?? "—",
            customer.Phone,
            order.DueDate,
            order.DueTime,
            order.Status,
            order.Total.Money(),
            order.Prepayment.Money(),
            order.Balance.Money(),
            order.Note,
            order.ProducedDate,
            order.CompletedDate,
            items,
            string.Join(", ", items.Select(i => $"{i.ProductName} × {i.Quantity:0.###}")),
            cost?.Money(),
            cost is { } value ? (order.Total - value).Money() : null);
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
