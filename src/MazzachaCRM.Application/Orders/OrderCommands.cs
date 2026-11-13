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
            command.Note,
            command.Items.Select(i => i.ToSpec()));

        if (command.Prepayment > 0)
            order.AddPayment(DateOnly.FromDateTime(DateTime.Now), command.Prepayment, "Предоплата");

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
    string? Note,
    IReadOnlyList<OrderItemInput> Items) : ICommand<Unit>;

public sealed class UpdateOrderValidator : AbstractValidator<UpdateOrderCommand>
{
    public UpdateOrderValidator()
    {
        RuleFor(x => x.CustomerId).NotEmpty().WithMessage("Выберите клиента.");
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

        order.UpdateDetails(command.CustomerId, command.DueDate, command.DueTime, command.Note);

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
/// «Готов» — партии изготовлены в указанный день, сырьё списано, выручка записана в тот же день;
/// «Выдан» — только отметка; возврат в работу — партии удалены, сырьё возвращено;
/// отмена — изготовленное остаётся обычным остатком, который можно продать или списать.
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
                    batch.SetWriteOffForDate(batch.ProductionDate, 0, null);
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

            case OrderStatus.Ready or OrderStatus.Delivered:
                if (orderBatches.Count == 0)
                {
                    var context = await costing.LoadTrackedAsync(ct);
                    orderBatches = order.Items
                        .Select(item => production.Produce(
                            context, order.ProducedDate!.Value, item.ProductId, item.Quantity, item.UnitPrice, order.Id, item.Id))
                        .ToList();
                }

                OrderSales.Apply(order, orderBatches);
                break;
        }

        return Unit.Value;
    }
}

/// <summary>Продажа по заказу записывается в день готовности: за всё, кроме возвращённого магазином.</summary>
internal static class OrderSales
{
    public static void Apply(Order order, IReadOnlyCollection<ProductionBatch> orderBatches)
    {
        foreach (var (batch, item) in Match(order, orderBatches))
        {
            batch.SetSalesForDate(
                batch.ProductionDate,
                item.SoldQuantity > 0 ? [new SaleEntry(item.SoldQuantity, item.UnitPrice)] : []);
        }
    }

    public static List<(ProductionBatch Batch, OrderItem Item)> Match(Order order, IReadOnlyCollection<ProductionBatch> orderBatches)
    {
        var unmatched = order.Items.ToList();
        var pairs = new List<(ProductionBatch Batch, OrderItem Item)>();

        foreach (var batch in orderBatches.Where(b => b.OrderItemId != null))
        {
            var item = unmatched.FirstOrDefault(i => i.Id == batch.OrderItemId);
            if (item is null)
                continue;
            unmatched.Remove(item);
            pairs.Add((batch, item));
        }

        // Партии, созданные до появления связи с позицией, сопоставляются по изделию.
        foreach (var batch in orderBatches.Where(b => b.OrderItemId == null))
        {
            var item = unmatched.FirstOrDefault(i => i.ProductId == batch.ProductId);
            if (item is null)
                continue;
            unmatched.Remove(item);
            pairs.Add((batch, item));
        }

        return pairs;
    }
}

// ---------- Оплаты ----------

public sealed record AddOrderPaymentCommand(Guid OrderId, DateOnly Date, decimal Amount, string? Note) : ICommand<Guid>;

public sealed class AddOrderPaymentValidator : AbstractValidator<AddOrderPaymentCommand>
{
    public AddOrderPaymentValidator()
    {
        RuleFor(x => x.Amount).GreaterThan(0).WithName("Сумма оплаты");
        RuleFor(x => x.Note).MaximumLength(300).WithName("Комментарий");
    }
}

public sealed class AddOrderPaymentHandler(IOrderRepository orders) : ICommandHandler<AddOrderPaymentCommand, Guid>
{
    public async Task<Guid> Handle(AddOrderPaymentCommand command, CancellationToken ct)
    {
        var order = await orders.GetByIdAsync(command.OrderId, ct)
            ?? throw new NotFoundException("Заказ не найден.");
        return order.AddPayment(command.Date, command.Amount, command.Note).Id;
    }
}

public sealed record RemoveOrderPaymentCommand(Guid OrderId, Guid PaymentId) : ICommand<Unit>;

public sealed class RemoveOrderPaymentHandler(IOrderRepository orders) : ICommandHandler<RemoveOrderPaymentCommand, Unit>
{
    public async Task<Unit> Handle(RemoveOrderPaymentCommand command, CancellationToken ct)
    {
        var order = await orders.GetByIdAsync(command.OrderId, ct)
            ?? throw new NotFoundException("Заказ не найден.");
        order.RemovePayment(command.PaymentId);
        return Unit.Value;
    }
}

// ---------- Отчёт магазина ----------

/// <param name="WrittenOff">
/// Сколько из возврата списать сразу, в день отправки: тогда оно не переходит в остатки следующих дней.
/// Остальной возврат становится остатком, который можно продать или списать позже.
/// </param>
public sealed record ShopReportItemInput(Guid ItemId, decimal Sold, decimal WrittenOff = 0);

public sealed record SubmitShopReportCommand(Guid OrderId, DateOnly Date, IReadOnlyList<ShopReportItemInput> Items)
    : ICommand<Unit>;

public sealed class SubmitShopReportValidator : AbstractValidator<SubmitShopReportCommand>
{
    public SubmitShopReportValidator()
    {
        RuleFor(x => x.Items).NotEmpty().WithMessage("Укажите, сколько продано.");
        RuleForEach(x => x.Items).ChildRules(item =>
        {
            item.RuleFor(i => i.Sold).GreaterThanOrEqualTo(0).WithName("Продано");
            item.RuleFor(i => i.WrittenOff).GreaterThanOrEqualTo(0).WithName("Списать");
        });
    }
}

/// <summary>
/// Магазин сообщил, сколько продано: остальное считается возвратом. Выручка дня отправки
/// уменьшается на возвраты, а возвращённые изделия становятся остатком, который можно продать или списать.
/// </summary>
public sealed class SubmitShopReportHandler(IOrderRepository orders, IProductionBatchRepository batches)
    : ICommandHandler<SubmitShopReportCommand, Unit>
{
    public async Task<Unit> Handle(SubmitShopReportCommand command, CancellationToken ct)
    {
        var order = await orders.GetByIdAsync(command.OrderId, ct)
            ?? throw new NotFoundException("Заказ не найден.");

        var returned = new Dictionary<Guid, decimal>();
        var writeOffs = new Dictionary<Guid, decimal>();
        foreach (var input in command.Items)
        {
            var item = order.Items.FirstOrDefault(i => i.Id == input.ItemId)
                ?? throw new NotFoundException("Позиция заказа не найдена.");
            if (input.Sold > item.Quantity)
                throw new DomainException($"Продано не может быть больше, чем отправлено ({item.Quantity:0.###}).");
            var itemReturned = item.Quantity - input.Sold;
            if (input.WrittenOff > itemReturned)
                throw new DomainException($"Списать можно не больше, чем вернули ({itemReturned:0.###}).");
            returned[item.Id] = itemReturned;
            writeOffs[item.Id] = input.WrittenOff;
        }

        order.RecordReturns(command.Date, returned);

        var orderBatches = await batches.ListByOrderAsync(order.Id, ct);
        foreach (var (batch, item) in OrderSales.Match(order, orderBatches))
        {
            // Сначала убираем прежнее списание, чтобы оно не мешало пересчитать продажу.
            batch.SetWriteOffForDate(batch.ProductionDate, 0, null);
            batch.SetSalesForDate(
                batch.ProductionDate,
                item.SoldQuantity > 0 ? [new SaleEntry(item.SoldQuantity, item.UnitPrice)] : []);
            batch.SetWriteOffForDate(batch.ProductionDate, writeOffs.GetValueOrDefault(item.Id), "Возврат, списан сразу");
        }

        return Unit.Value;
    }
}

// ---------- Повтор заказа ----------

public sealed record RepeatOrderCommand(Guid OrderId, DateOnly DueDate) : ICommand<Guid>;

/// <summary>Новый заказ с тем же клиентом и позициями: удобно для регулярных поставок в магазин.</summary>
public sealed class RepeatOrderHandler(IOrderRepository orders, IProductRepository products)
    : ICommandHandler<RepeatOrderCommand, Guid>
{
    public async Task<Guid> Handle(RepeatOrderCommand command, CancellationToken ct)
    {
        var source = await orders.GetByIdAsync(command.OrderId, ct)
            ?? throw new NotFoundException("Заказ не найден.");

        var items = source.Items.Select(i => new OrderItemInput(i.ProductId, i.Quantity, i.UnitPrice, i.Note)).ToList();
        await OrderRules.EnsureSellableAsync(products, items, ct);

        var order = Order.Create(
            await orders.NextNumberAsync(ct),
            source.CustomerId,
            command.DueDate,
            source.DueTime,
            null,
            items.Select(i => i.ToSpec()));

        orders.Add(order);
        return order.Id;
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
