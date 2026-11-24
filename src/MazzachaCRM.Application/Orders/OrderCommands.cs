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

/// <param name="FromBatchId">
/// Свободная строка дня, из которой взять готовые изделия. Пусто — изделие приготовят под заказ.
/// </param>
public sealed record OrderItemInput(Guid ProductId, decimal Quantity, decimal UnitPrice, string? Note, Guid? FromBatchId = null)
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
    /// <summary>Закрепляет готовые изделия за теми позициями, где это указано. Позиции идут в том же порядке, что и во вводе.</summary>
    public static async Task ReserveFromStockAsync(
        OrderStockService stock, Order order, IReadOnlyList<OrderItemInput> inputs, CancellationToken ct)
    {
        var items = order.Items.ToList();
        for (var index = 0; index < items.Count; index++)
        {
            if (inputs[index].FromBatchId is { } batchId)
                await stock.ReserveAsync(order, items[index], batchId, ct);
        }
    }

    /// <summary>Снимает с партии продажу и списание, которые записал заказ в день готовности.</summary>
    public static void ClearOrderEntries(ProductionBatch batch, DateOnly? producedDate)
    {
        batch.ClearSales();
        if (producedDate is { } date && date >= batch.ProductionDate)
            batch.SetWriteOffForDate(date, 0, null);
    }

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

/// <param name="DueDate">День выдачи или доставки.</param>
/// <param name="ProductionDate">День приготовления. Пусто — тот же, что и день выдачи.</param>
public sealed record CreateOrderCommand(
    Guid? CustomerId,
    string? NewCustomerName,
    string? NewCustomerPhone,
    DateOnly DueDate,
    TimeOnly? DueTime,
    decimal Prepayment,
    string? Note,
    IReadOnlyList<OrderItemInput> Items,
    DateOnly? ProductionDate = null) : ICommand<Guid>;

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
        RuleFor(x => x.DueDate)
            .GreaterThanOrEqualTo(x => x.ProductionDate!.Value)
            .When(x => x.ProductionDate is not null)
            .WithMessage("День выдачи не может быть раньше дня приготовления.");
    }
}

public sealed class CreateOrderHandler(
    IOrderRepository orders,
    ICustomerRepository customers,
    IProductRepository products,
    OrderStockService stock)
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
            command.ProductionDate ?? command.DueDate,
            command.DueDate,
            command.DueTime,
            command.Note,
            command.Items.Select(i => i.ToSpec()));

        if (command.Prepayment > 0)
            order.AddPayment(DateOnly.FromDateTime(DateTime.Now), command.Prepayment, "Предоплата");

        orders.Add(order);
        await OrderRules.ReserveFromStockAsync(stock, order, command.Items, ct);
        return order.Id;
    }
}

// ---------- Изменение ----------

/// <param name="ProductionDate">
/// День приготовления. Пусто — у изготовленного заказа остаётся прежним, у остальных совпадает с днём выдачи.
/// </param>
public sealed record UpdateOrderCommand(
    Guid Id,
    Guid CustomerId,
    DateOnly DueDate,
    TimeOnly? DueTime,
    string? Note,
    IReadOnlyList<OrderItemInput> Items,
    DateOnly? ProductionDate = null) : ICommand<Unit>;

public sealed class UpdateOrderValidator : AbstractValidator<UpdateOrderCommand>
{
    public UpdateOrderValidator()
    {
        RuleFor(x => x.CustomerId).NotEmpty().WithMessage("Выберите клиента.");
        RuleFor(x => x.Note).MaximumLength(1000).WithName("Заметка");
        RuleFor(x => x.Items).NotEmpty().WithMessage("Добавьте в заказ хотя бы одно изделие.");
        RuleForEach(x => x.Items).SetValidator(new OrderItemInputValidator());
        RuleFor(x => x.DueDate)
            .GreaterThanOrEqualTo(x => x.ProductionDate!.Value)
            .When(x => x.ProductionDate is not null)
            .WithMessage("День выдачи не может быть раньше дня приготовления.");
    }
}

public sealed class UpdateOrderHandler(
    IOrderRepository orders,
    ICustomerRepository customers,
    IProductRepository products,
    IProductionBatchRepository batches,
    OrderStockService stock)
    : ICommandHandler<UpdateOrderCommand, Unit>
{
    public async Task<Unit> Handle(UpdateOrderCommand command, CancellationToken ct)
    {
        var order = await orders.GetByIdAsync(command.Id, ct)
            ?? throw new NotFoundException("Заказ не найден.");
        _ = await customers.GetByIdAsync(command.CustomerId, ct)
            ?? throw new NotFoundException("Клиент не найден.");

        var productionDate = command.ProductionDate ?? (order.IsProduced ? order.ProductionDate : command.DueDate);
        var productionDateChanged = productionDate != order.ProductionDate;
        order.UpdateDetails(command.CustomerId, productionDate, command.DueDate, command.DueTime, command.Note);

        var specs = command.Items.Select(i => i.ToSpec()).ToList();
        var orderBatches = await batches.ListByOrderAsync(order.Id, ct);

        // Откуда сейчас взята каждая позиция: закреплённая партия или «приготовить».
        var reserved = orderBatches.Where(b => b.ReservedFromStock).ToList();
        var currentSources = order.Items
            .Select(item => reserved.FirstOrDefault(b => b.OrderItemId == item.Id)?.Id)
            .ToList();
        var newSources = command.Items.Select(i => i.FromBatchId).ToList();

        // Новый день приготовления: взятое из готового проверяется заново — оно должно быть доступно в этот день.
        var mustReserveAgain = productionDateChanged && reserved.Count > 0;
        if (!order.HasSameItems(specs) || !currentSources.SequenceEqual(newSources) || mustReserveAgain)
        {
            await OrderRules.EnsureSellableAsync(products, command.Items, ct);

            // Сначала всё закреплённое возвращается на витрину, затем закрепляется заново по новому составу.
            foreach (var batch in reserved)
                batch.ReleaseFromOrder();
            order.SetItems(specs);
            await OrderRules.ReserveFromStockAsync(stock, order, command.Items, ct);
            await stock.MergeBackAsync(reserved, ct);
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
/// «Готов» — недостающее изготовлено в указанный день, сырьё списано, выручка записана в тот же день;
/// «Выдан» — только отметка; возврат в работу — испечённое под заказ удалено, сырьё возвращено,
/// а взятое из готового остаётся закреплённым; отмена — изготовленное остаётся обычным остатком,
/// взятое из готового возвращается на витрину.
/// </summary>
public sealed class ChangeOrderStatusHandler(
    IOrderRepository orders,
    IProductionBatchRepository batches,
    CostingContextLoader costing,
    BatchProductionService production,
    OrderStockService stock)
    : ICommandHandler<ChangeOrderStatusCommand, Unit>
{
    public async Task<Unit> Handle(ChangeOrderStatusCommand command, CancellationToken ct)
    {
        var order = await orders.GetByIdAsync(command.Id, ct)
            ?? throw new NotFoundException("Заказ не найден.");

        if (order.Status == command.Status)
            return Unit.Value;

        var previousProducedDate = order.ProducedDate;
        order.ChangeStatus(command.Status, command.Date);
        var orderBatches = await batches.ListByOrderAsync(order.Id, ct);

        switch (command.Status)
        {
            case OrderStatus.Cancelled:
                foreach (var batch in orderBatches)
                {
                    OrderRules.ClearOrderEntries(batch, previousProducedDate);
                    batch.ReleaseFromOrder();
                }

                await stock.MergeBackAsync(orderBatches, ct);
                break;

            case OrderStatus.New or OrderStatus.InProgress:
                CostingContext? context = null;
                foreach (var batch in orderBatches)
                {
                    OrderRules.ClearOrderEntries(batch, previousProducedDate);
                    if (batch.ReservedFromStock)
                        continue;

                    context ??= await costing.LoadTrackedAsync(ct);
                    await production.RevertConsumptionAsync(context, batch.Id, ct);
                    batches.Remove(batch);
                }

                break;

            case OrderStatus.Ready or OrderStatus.Delivered:
                // Печём только то, что не взято из готового и ещё не изготовлено.
                var covered = OrderSales.Match(order, orderBatches).Select(pair => pair.Item.Id).ToHashSet();
                var missing = order.Items.Where(item => !covered.Contains(item.Id)).ToList();
                if (missing.Count > 0)
                {
                    var costingContext = await costing.LoadTrackedAsync(ct);
                    foreach (var item in missing)
                    {
                        orderBatches.Add(production.Produce(
                            costingContext, order.ProducedDate!.Value, item.ProductId, item.Quantity, item.UnitPrice, order.Id, item.Id));
                    }
                }

                OrderSales.Apply(order, orderBatches);
                break;
        }

        return Unit.Value;
    }
}

/// <summary>
/// Продажа по заказу записывается в день готовности заказа: за всё, кроме возвращённого магазином.
/// Для изделий, взятых из готового, это может быть позже дня, когда их пекли.
/// </summary>
internal static class OrderSales
{
    public static void Apply(Order order, IReadOnlyCollection<ProductionBatch> orderBatches)
    {
        var date = order.ProducedDate
            ?? throw new DomainException("Заказ ещё не готов: продажу записывать рано.");

        foreach (var (batch, item) in Match(order, orderBatches))
        {
            if (date < batch.ProductionDate)
                throw new DomainException(
                    "День готовности заказа не может быть раньше дня, когда приготовили изделия, взятые из готового.");

            batch.SetSalesForDate(date, item.SoldQuantity > 0 ? [new SaleEntry(item.SoldQuantity, item.UnitPrice)] : []);
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
/// Сколько из возврата списать сразу, в день отправки. Остальной возврат становится остатком дня отправки:
/// его можно перенести на другой день и там продать или списать.
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
/// уменьшается на возвраты, а возвращённые изделия становятся остатком этого дня, который можно
/// перенести, продать или списать.
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

        var readyDate = order.ProducedDate!.Value;
        var orderBatches = await batches.ListByOrderAsync(order.Id, ct);
        foreach (var (batch, item) in OrderSales.Match(order, orderBatches))
        {
            // Сначала убираем прежнее списание, чтобы оно не мешало пересчитать продажу.
            batch.SetWriteOffForDate(readyDate, 0, null);
            batch.SetSalesForDate(
                readyDate,
                item.SoldQuantity > 0 ? [new SaleEntry(item.SoldQuantity, item.UnitPrice)] : []);
            batch.SetWriteOffForDate(readyDate, writeOffs.GetValueOrDefault(item.Id), "Возврат, списан сразу");
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

public sealed class DeleteOrderHandler(
    IOrderRepository orders,
    IProductionBatchRepository batches,
    OrderStockService stock)
    : ICommandHandler<DeleteOrderCommand, Unit>
{
    public async Task<Unit> Handle(DeleteOrderCommand command, CancellationToken ct)
    {
        var order = await orders.GetByIdAsync(command.Id, ct)
            ?? throw new NotFoundException("Заказ не найден.");

        if (order.IsProduced)
            throw new DomainException(
                "Заказ уже изготовлен. Чтобы удалить его, сначала отмените заказ или верните его в работу.");

        // Готовые изделия, закреплённые за заказом, возвращаются на витрину.
        var orderBatches = await batches.ListByOrderAsync(order.Id, ct);
        foreach (var batch in orderBatches)
        {
            batch.ClearSales();
            batch.ReleaseFromOrder();
        }

        await stock.MergeBackAsync(orderBatches, ct);
        orders.Remove(order);
        return Unit.Value;
    }
}
