using FluentValidation;
using MazzachaCRM.Application.Abstractions;
using MazzachaCRM.Application.Common;
using MazzachaCRM.Domain.Common;
using MazzachaCRM.Domain.Orders;
using MazzachaCRM.Domain.Production;

namespace MazzachaCRM.Application.Production;

// Непроданный остаток сам на следующие дни не переходит. Здесь команды, которыми его
// переносят, отменяют перенос или списывают как непроданный.

internal static class LeftoverRules
{
    /// <summary>
    /// Изделия, закреплённые за заказом, который ещё не готов, уйдут в заказ: это не остаток.
    /// Остатком становится только то, что вернул магазин после готовности заказа.
    /// </summary>
    public static async Task<Order?> EnsureLeftoverAsync(
        ProductionBatch batch, DateOnly? untilDate, IOrderRepository orders, CancellationToken ct)
    {
        if (batch.OrderId is not { } orderId)
            return null;

        var order = await orders.GetByIdAsync(orderId, ct);
        if (order?.ProducedDate is not { } readyDate || (untilDate is { } date && date <= readyDate))
            throw new DomainException(
                $"Изделия закреплены за заказом №{order?.Number}: они уйдут в заказ, переносить или списывать их не нужно.");
        return order;
    }
}

// ---------- Перенос ----------

/// <summary>Переносит остаток указанных строк по день <paramref name="ToDate"/> включительно.</summary>
public sealed record CarryLeftoversCommand(IReadOnlyList<Guid> BatchIds, DateOnly ToDate) : ICommand<int>;

public sealed class CarryLeftoversValidator : AbstractValidator<CarryLeftoversCommand>
{
    public CarryLeftoversValidator()
    {
        RuleFor(x => x.BatchIds).NotEmpty().WithMessage("Отметьте, что перенести.");
    }
}

public sealed class CarryLeftoversHandler(IProductionBatchRepository batches, IOrderRepository orders)
    : ICommandHandler<CarryLeftoversCommand, int>
{
    public async Task<int> Handle(CarryLeftoversCommand command, CancellationToken ct)
    {
        foreach (var id in command.BatchIds.Distinct())
        {
            var batch = await batches.GetByIdAsync(id, ct)
                ?? throw new NotFoundException("Строка не найдена: возможно, её уже удалили.");
            await LeftoverRules.EnsureLeftoverAsync(batch, command.ToDate, orders, ct);
            batch.CarryTo(command.ToDate);
        }

        return command.BatchIds.Distinct().Count();
    }
}

// ---------- Отмена переноса ----------

/// <summary>Остаток снова заканчивается в день <paramref name="LastDay"/>: из следующих дней он пропадает.</summary>
public sealed record CancelCarryCommand(Guid BatchId, DateOnly LastDay) : ICommand<Unit>;

public sealed class CancelCarryHandler(IProductionBatchRepository batches) : ICommandHandler<CancelCarryCommand, Unit>
{
    public async Task<Unit> Handle(CancelCarryCommand command, CancellationToken ct)
    {
        var batch = await batches.GetByIdAsync(command.BatchId, ct)
            ?? throw new NotFoundException("Строка не найдена.");
        batch.CancelCarryAfter(command.LastDay);
        return Unit.Value;
    }
}

// ---------- Списание непроданного ----------

/// <summary>
/// Списывает весь остаток указанных строк в последний день, когда их можно было продать.
/// Убыток от этого не меняется — себестоимость уже в затратах дня изготовления, — но остаток
/// перестаёт числиться и больше не напоминает о себе.
/// </summary>
public sealed record WriteOffLeftoversCommand(IReadOnlyList<Guid> BatchIds, string? Reason) : ICommand<int>;

public sealed class WriteOffLeftoversValidator : AbstractValidator<WriteOffLeftoversCommand>
{
    public WriteOffLeftoversValidator()
    {
        RuleFor(x => x.BatchIds).NotEmpty().WithMessage("Отметьте, что списать.");
        RuleFor(x => x.Reason).MaximumLength(300).WithName("Причина");
    }
}

public sealed class WriteOffLeftoversHandler(IProductionBatchRepository batches, IOrderRepository orders)
    : ICommandHandler<WriteOffLeftoversCommand, int>
{
    public async Task<int> Handle(WriteOffLeftoversCommand command, CancellationToken ct)
    {
        var count = 0;
        foreach (var id in command.BatchIds.Distinct())
        {
            var batch = await batches.GetByIdAsync(id, ct)
                ?? throw new NotFoundException("Строка не найдена: возможно, её уже удалили.");
            var order = await LeftoverRules.EnsureLeftoverAsync(batch, null, orders, ct);
            if (batch.RemainingQuantity <= 0)
                continue;

            // Возврат по заказу списывается не раньше дня, когда заказ продал эти изделия.
            var day = order?.ProducedDate is { } readyDate && readyDate > batch.LastAvailableDay
                ? readyDate
                : batch.LastAvailableDay;
            var reason = batch.WriteOffs.FirstOrDefault(w => w.Date == day)?.Reason
                         ?? (string.IsNullOrWhiteSpace(command.Reason) ? "Не продано" : command.Reason);
            batch.SetWriteOffForDate(day, batch.WrittenOffOn(day) + batch.RemainingQuantity, reason);
            count++;
        }

        return count;
    }
}
