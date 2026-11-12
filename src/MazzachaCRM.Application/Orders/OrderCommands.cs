using FluentValidation;
using MazzachaCRM.Application.Abstractions;
using MazzachaCRM.Application.Common;
using MazzachaCRM.Application.Production;
using MazzachaCRM.Domain.Common;
using MazzachaCRM.Domain.Customers;
using MazzachaCRM.Domain.Orders;
using MazzachaCRM.Domain.Production;
using MazzachaCRM.Domain.Products;

namespace MazzachaCRM.Application.Orders;

public sealed record OrderItemInput(Guid ProductId, decimal Quantity, decimal UnitPrice, string? Note)
{
    public OrderItemSpec ToSpec() => new(ProductId, Quantity, UnitPrice, Note);
}

public sealed class OrderItemInputValidator : AbstractValidator<OrderItemInput>
{
    public OrderItemInputValidator()
    {
        RuleFor(i => i.ProductId).NotEmpty().WithMessage("Выберите изделие.");
        RuleFor(i => i.Quantity).GreaterThan(0).WithName("Количество");
        RuleFor(i => i.UnitPrice).GreaterThanOrEqualTo(0).WithName("Цена");
        RuleFor(i => i.Note).MaximumLength(500).WithName("Пожелания");
    }
}

internal static class OrderRules
{
    /// <summary>В заказ можно добавить только существующие готовые изделия.</summary>
    public static async Task EnsureSellableAsync(
        IProductRepository products, IEnumerable<OrderItemInput> items, CancellationToken ct)
    {
        var all = (await products.ListAsync(ct)).ToDictionary(p => p.Id);
        foreach (var item in items)
        {
            if (!all.TryGetValue(item.ProductId, out var product))
                throw new NotFoundException("Изделие из заказа не найдено.");
            if (product.Type != ProductType.Finished)
                throw new DomainException($"«{product.Name}» — полуфабрикат, его нельзя добавить в заказ.");
        }
    }
}

// ---------- Создание ----------

public sealed record CreateOrderCommand(
    Guid? CustomerId,
    string? NewCustomerName,
    string? NewCustomerPhone,
    DateOnly DueDate,
    TimeOnly? DueTime,
    decimal Prepayment,
    string? Note,
    IReadOnlyList<OrderItemInput> Items) : ICommand<Guid>;

public sealed class CreateOrderValidator : AbstractValidator<CreateOrderCommand>
{
    public CreateOrderValidator()
    {
        RuleFor(x => x.NewCustomerName)
            .NotEmpty()
            .When(x => x.CustomerId is null)
            .WithMessage("Выберите клиента или введите имя нового.");
        RuleFor(x => x.NewCustomerName).MaximumLength(200).WithName("Имя клиента");
        RuleFor(x => x.NewCustomerPhone).MaximumLength(50).WithName("Телефон");
        RuleFor(x => x.Prepayment).GreaterThanOrEqualTo(0).WithName("Предоплата");
        RuleFor(x => x.Note).MaximumLength(1000).WithName("Заметка");
        RuleFor(x => x.Items).NotEmpty().WithMessage("Добавьте в заказ хотя бы одно изделие.");
        RuleForEach(x => x.Items).SetValidator(new OrderItemInputValidator());
    }
}

public sealed class CreateOrderHandler(
    IOrderRepository orders,
    ICustomerRepository customers,
    IProductRepository products)
    : ICommandHandler<CreateOrderCommand, Guid>
{
    public async Task<Guid> Handle(CreateOrderCommand command, CancellationToken ct)
    {
        Guid customerId;
        if (command.CustomerId is { } existingId)
        {
            var customer = await customers.GetByIdAsync(existingId, ct)
                ?? throw new NotFoundException("Клиент не найден.");
            customerId = customer.Id;
        }
        else
        {
            var customer = Customer.Create(command.NewCustomerName!, command.NewCustomerPhone, null);
            customers.Add(customer);
            customerId = customer.Id;
        }

        await OrderRules.EnsureSellableAsync(products, command.Items, ct);

        var order = Order.Create(
            await orders.NextNumberAsync(ct),
            customerId,
            command.DueDate,
            command.DueTime,
            command.Prepayment,
            command.Note,
            command.Items.Select(i => i.ToSpec()));

        orders.Add(order);
        return order.Id;
    }
}

// ---------- Изменение ----------

public sealed record UpdateOrderCommand(
    Guid Id,
    Guid CustomerId,
    DateOnly DueDate,
    TimeOnly? DueTime,
    decimal Prepayment,
    string? Note,
    IReadOnlyList<OrderItemInput> Items) : ICommand<Unit>;

public sealed class UpdateOrderValidator : AbstractValidator<UpdateOrderCommand>
{
    public UpdateOrderValidator()
    {
        RuleFor(x => x.CustomerId).NotEmpty().WithMessage("Выберите клиента.");
        RuleFor(x => x.Prepayment).GreaterThanOrEqualTo(0).WithName("Предоплата");
        RuleFor(x => x.Note).MaximumLength(1000).WithName("Заметка");
        RuleFor(x => x.Items).NotEmpty().WithMessage("Добавьте в заказ хотя бы одно изделие.");
        RuleForEach(x => x.Items).SetValidator(new OrderItemInputValidator());
    }
}

public sealed class UpdateOrderHandler(
    IOrderRepository orders,
    ICustomerRepository customers,
    IProductRepository products)
    : ICommandHandler<UpdateOrderCommand, Unit>
{
    public async Task<Unit> Handle(UpdateOrderCommand command, CancellationToken ct)
    {
        var order = await orders.GetByIdAsync(command.Id, ct)
            ?? throw new NotFoundException("Заказ не найден.");
        _ = await customers.GetByIdAsync(command.CustomerId, ct)
            ?? throw new NotFoundException("Клиент не найден.");

        order.UpdateDetails(command.CustomerId, command.DueDate, command.DueTime, command.Prepayment, command.Note);

        var specs = command.Items.Select(i => i.ToSpec()).ToList();
        if (!order.HasSameItems(specs))
        {
            await OrderRules.EnsureSellableAsync(products, command.Items, ct);
            order.SetItems(specs);
        }

        return Unit.Value;
    }
}

// ---------- Смена статуса ----------

public sealed record ChangeOrderStatusCommand(Guid Id, OrderStatus Status, DateOnly Date) : ICommand<Unit>;

public sealed class ChangeOrderStatusValidator : AbstractValidator<ChangeOrderStatusCommand>
{
    public ChangeOrderStatusValidator()
    {
        RuleFor(x => x.Status).IsInEnum().WithName("Статус");
    }
}

/// <summary>
/// Приводит партии заказа в соответствие со статусом:
/// «Готов» — партии изготовлены, сырьё списано; «Выдан» — по партиям записана продажа;
/// возврат в работу — партии удалены, сырьё возвращено; отмена — изготовленное
/// остаётся обычным остатком, который можно продать или списать.
/// </summary>
public sealed class ChangeOrderStatusHandler(
    IOrderRepository orders,
    IProductionBatchRepository batches,
    CostingContextLoader costing,
    BatchProductionService production)
    : ICommandHandler<ChangeOrderStatusCommand, Unit>
{
    public async Task<Unit> Handle(ChangeOrderStatusCommand command, CancellationToken ct)
    {
        var order = await orders.GetByIdAsync(command.Id, ct)
            ?? throw new NotFoundException("Заказ не найден.");

        if (order.Status == command.Status)
            return Unit.Value;

        order.ChangeStatus(command.Status, command.Date);
        var orderBatches = await batches.ListByOrderAsync(order.Id, ct);

        switch (command.Status)
        {
            case OrderStatus.Cancelled:
                foreach (var batch in orderBatches)
                {
                    batch.ClearSales();
                    batch.ReleaseFromOrder();
                }

                break;

            case OrderStatus.New or OrderStatus.InProgress:
                if (orderBatches.Count > 0)
                {
                    var context = await costing.LoadTrackedAsync(ct);
                    foreach (var batch in orderBatches)
                    {
                        await production.RevertConsumptionAsync(context, batch.Id, ct);
                        batches.Remove(batch);
                    }
                }

                break;

            case OrderStatus.Ready or OrderStatus.Completed:
                if (orderBatches.Count == 0)
                {
                    var context = await costing.LoadTrackedAsync(ct);
                    orderBatches = order.Items
                        .Select(item => production.Produce(
                            context, command.Date, item.ProductId, item.Quantity, item.UnitPrice, order.Id))
                        .ToList();
                }

                foreach (var batch in orderBatches)
                {
                    if (command.Status == OrderStatus.Completed)
                        batch.SetSalesForDate(command.Date, [new SaleEntry(batch.Quantity, batch.UnitPrice)]);
                    else
                        batch.ClearSales();
                }

                break;
        }

        return Unit.Value;
    }
}

// ---------- Удаление ----------

public sealed record DeleteOrderCommand(Guid Id) : ICommand<Unit>;

public sealed class DeleteOrderHandler(IOrderRepository orders) : ICommandHandler<DeleteOrderCommand, Unit>
{
    public async Task<Unit> Handle(DeleteOrderCommand command, CancellationToken ct)
    {
        var order = await orders.GetByIdAsync(command.Id, ct)
            ?? throw new NotFoundException("Заказ не найден.");

        if (order.IsProduced)
            throw new DomainException(
                "Заказ уже изготовлен. Чтобы удалить его, сначала отмените заказ или верните его в работу.");

        orders.Remove(order);
        return Unit.Value;
    }
}
