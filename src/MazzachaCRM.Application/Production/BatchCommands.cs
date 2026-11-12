using FluentValidation;
using MazzachaCRM.Application.Abstractions;
using MazzachaCRM.Application.Common;
using MazzachaCRM.Domain.Common;
using MazzachaCRM.Domain.Production;

namespace MazzachaCRM.Application.Production;

// ---------- Новая строка дня ----------

public sealed record CreateBatchCommand(
    DateOnly Date,
    Guid ProductId,
    decimal Quantity,
    decimal? UnitPrice,
    decimal? SoldQuantity) : ICommand<Guid>;

public sealed class CreateBatchValidator : AbstractValidator<CreateBatchCommand>
{
    public CreateBatchValidator()
    {
        RuleFor(x => x.ProductId).NotEmpty().WithMessage("Выберите изделие.");
        RuleFor(x => x.Quantity).GreaterThan(0).WithName("Количество");
        RuleFor(x => x.UnitPrice).GreaterThanOrEqualTo(0).WithName("Цена");
        RuleFor(x => x.SoldQuantity).GreaterThanOrEqualTo(0).WithName("Продано");
    }
}

public sealed class CreateBatchHandler(CostingContextLoader costing, BatchProductionService production)
    : ICommandHandler<CreateBatchCommand, Guid>
{
    public async Task<Guid> Handle(CreateBatchCommand command, CancellationToken ct)
    {
        var context = await costing.LoadTrackedAsync(ct);
        var batch = production.Produce(context, command.Date, command.ProductId, command.Quantity, command.UnitPrice);

        if (command.SoldQuantity is > 0)
            batch.SetSalesForDate(command.Date, [new SaleEntry(command.SoldQuantity.Value, batch.UnitPrice)]);

        return batch.Id;
    }
}

// ---------- Изменение количества и цены ----------

public sealed record UpdateBatchCommand(Guid Id, decimal Quantity, decimal UnitPrice) : ICommand<Unit>;

public sealed class UpdateBatchValidator : AbstractValidator<UpdateBatchCommand>
{
    public UpdateBatchValidator()
    {
        RuleFor(x => x.Quantity).GreaterThan(0).WithName("Количество");
        RuleFor(x => x.UnitPrice).GreaterThanOrEqualTo(0).WithName("Цена");
    }
}

public sealed class UpdateBatchHandler(
    IProductionBatchRepository batches,
    CostingContextLoader costing,
    BatchProductionService production)
    : ICommandHandler<UpdateBatchCommand, Unit>
{
    public async Task<Unit> Handle(UpdateBatchCommand command, CancellationToken ct)
    {
        var batch = await batches.GetByIdAsync(command.Id, ct)
            ?? throw new NotFoundException("Строка не найдена.");
        BatchRules.EnsureNotOrderBatch(batch);

        if (command.UnitPrice != batch.UnitPrice)
            batch.ChangePrice(command.UnitPrice);

        if (command.Quantity != batch.Quantity)
        {
            batch.ChangeQuantity(command.Quantity);

            // Списание сырья пересчитывается под новое количество.
            var context = await costing.LoadTrackedAsync(ct);
            await production.RevertConsumptionAsync(context, batch.Id, ct);
            production.Consume(context, batch);
        }

        return Unit.Value;
    }
}

// ---------- Удаление ----------

public sealed record DeleteBatchCommand(Guid Id) : ICommand<Unit>;

public sealed class DeleteBatchHandler(
    IProductionBatchRepository batches,
    CostingContextLoader costing,
    BatchProductionService production)
    : ICommandHandler<DeleteBatchCommand, Unit>
{
    public async Task<Unit> Handle(DeleteBatchCommand command, CancellationToken ct)
    {
        var batch = await batches.GetByIdAsync(command.Id, ct)
            ?? throw new NotFoundException("Строка не найдена.");
        BatchRules.EnsureNotOrderBatch(batch);

        var context = await costing.LoadTrackedAsync(ct);
        await production.RevertConsumptionAsync(context, batch.Id, ct);
        batches.Remove(batch);
        return Unit.Value;
    }
}

// ---------- Пересчёт себестоимости по текущему составу и ценам ----------

public sealed record RecalculateBatchCostCommand(Guid Id) : ICommand<Unit>;

public sealed class RecalculateBatchCostHandler(
    IProductionBatchRepository batches,
    CostingContextLoader costing,
    BatchProductionService production)
    : ICommandHandler<RecalculateBatchCostCommand, Unit>
{
    public async Task<Unit> Handle(RecalculateBatchCostCommand command, CancellationToken ct)
    {
        var batch = await batches.GetByIdAsync(command.Id, ct)
            ?? throw new NotFoundException("Строка не найдена.");

        var context = await costing.LoadTrackedAsync(ct);
        batch.UpdateCostSnapshot(context.Calculator.UnitCost(batch.ProductId));

        await production.RevertConsumptionAsync(context, batch.Id, ct);
        production.Consume(context, batch);
        return Unit.Value;
    }
}

// ---------- Продажи за день ----------

public sealed record SaleEntryInput(decimal Quantity, decimal UnitPrice);

public sealed record SetBatchSalesCommand(Guid BatchId, DateOnly Date, IReadOnlyList<SaleEntryInput> Entries)
    : ICommand<Unit>;

public sealed class SetBatchSalesValidator : AbstractValidator<SetBatchSalesCommand>
{
    public SetBatchSalesValidator()
    {
        RuleFor(x => x.Entries).NotNull().WithName("Продажи");
        RuleForEach(x => x.Entries).ChildRules(entry =>
        {
            entry.RuleFor(e => e.Quantity).GreaterThanOrEqualTo(0).WithName("Продано");
            entry.RuleFor(e => e.UnitPrice).GreaterThanOrEqualTo(0).WithName("Цена");
        });
    }
}

public sealed class SetBatchSalesHandler(IProductionBatchRepository batches)
    : ICommandHandler<SetBatchSalesCommand, Unit>
{
    public async Task<Unit> Handle(SetBatchSalesCommand command, CancellationToken ct)
    {
        var batch = await batches.GetByIdAsync(command.BatchId, ct)
            ?? throw new NotFoundException("Строка не найдена.");
        BatchRules.EnsureNotOrderBatch(batch);

        batch.SetSalesForDate(command.Date, command.Entries.Select(e => new SaleEntry(e.Quantity, e.UnitPrice)));
        return Unit.Value;
    }
}

// ---------- Списание за день ----------

public sealed record SetBatchWriteOffCommand(Guid BatchId, DateOnly Date, decimal Quantity, string? Reason)
    : ICommand<Unit>;

public sealed class SetBatchWriteOffValidator : AbstractValidator<SetBatchWriteOffCommand>
{
    public SetBatchWriteOffValidator()
    {
        RuleFor(x => x.Quantity).GreaterThanOrEqualTo(0).WithName("Списано");
        RuleFor(x => x.Reason).MaximumLength(300).WithName("Причина");
    }
}

public sealed class SetBatchWriteOffHandler(IProductionBatchRepository batches)
    : ICommandHandler<SetBatchWriteOffCommand, Unit>
{
    public async Task<Unit> Handle(SetBatchWriteOffCommand command, CancellationToken ct)
    {
        var batch = await batches.GetByIdAsync(command.BatchId, ct)
            ?? throw new NotFoundException("Строка не найдена.");
        BatchRules.EnsureNotOrderBatch(batch);

        batch.SetWriteOffForDate(command.Date, command.Quantity, command.Reason);
        return Unit.Value;
    }
}

internal static class BatchRules
{
    public static void EnsureNotOrderBatch(ProductionBatch batch)
    {
        if (batch.OrderId is not null)
            throw new DomainException("Эта партия изготовлена под заказ. Меняйте её через статус заказа.");
    }
}
