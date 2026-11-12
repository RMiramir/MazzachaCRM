using FluentValidation;
using MazzachaCRM.Application.Abstractions;
using MazzachaCRM.Application.Common;
using MazzachaCRM.Domain.Common;
using MazzachaCRM.Domain.Ingredients;

namespace MazzachaCRM.Application.Stock;

// ---------- Закупка ----------

public sealed record RegisterPurchaseCommand(
    Guid IngredientId,
    DateOnly Date,
    decimal Quantity,
    decimal TotalCost,
    bool UpdatePrice,
    string? Note) : ICommand<Guid>;

public sealed class RegisterPurchaseValidator : AbstractValidator<RegisterPurchaseCommand>
{
    public RegisterPurchaseValidator()
    {
        RuleFor(x => x.Quantity).GreaterThan(0).WithName("Количество");
        RuleFor(x => x.TotalCost).GreaterThanOrEqualTo(0).WithName("Сумма закупки");
        RuleFor(x => x.Note).MaximumLength(500).WithName("Комментарий");
    }
}

public sealed class RegisterPurchaseHandler(IIngredientRepository ingredients, IStockMovementRepository movements)
    : ICommandHandler<RegisterPurchaseCommand, Guid>
{
    public async Task<Guid> Handle(RegisterPurchaseCommand command, CancellationToken ct)
    {
        var ingredient = await ingredients.GetByIdAsync(command.IngredientId, ct)
            ?? throw new NotFoundException("Продукт не найден.");

        var movement = ingredient.RegisterPurchase(
            command.Date, command.Quantity, command.TotalCost, command.UpdatePrice, command.Note);
        movements.Add(movement);
        return movement.Id;
    }
}

// ---------- Инвентаризация ----------

public sealed record AdjustStockCommand(Guid IngredientId, DateOnly Date, decimal ActualQuantity, string? Note)
    : ICommand<Guid>;

public sealed class AdjustStockValidator : AbstractValidator<AdjustStockCommand>
{
    public AdjustStockValidator()
    {
        RuleFor(x => x.ActualQuantity).GreaterThanOrEqualTo(0).WithName("Фактический остаток");
        RuleFor(x => x.Note).MaximumLength(500).WithName("Комментарий");
    }
}

public sealed class AdjustStockHandler(IIngredientRepository ingredients, IStockMovementRepository movements)
    : ICommandHandler<AdjustStockCommand, Guid>
{
    public async Task<Guid> Handle(AdjustStockCommand command, CancellationToken ct)
    {
        var ingredient = await ingredients.GetByIdAsync(command.IngredientId, ct)
            ?? throw new NotFoundException("Продукт не найден.");

        var movement = ingredient.AdjustStockTo(command.Date, command.ActualQuantity, command.Note);
        movements.Add(movement);
        return movement.Id;
    }
}

// ---------- Списание сырья ----------

public sealed record WriteOffStockCommand(Guid IngredientId, DateOnly Date, decimal Quantity, string? Note)
    : ICommand<Guid>;

public sealed class WriteOffStockValidator : AbstractValidator<WriteOffStockCommand>
{
    public WriteOffStockValidator()
    {
        RuleFor(x => x.Quantity).GreaterThan(0).WithName("Количество");
        RuleFor(x => x.Note).MaximumLength(500).WithName("Комментарий");
    }
}

public sealed class WriteOffStockHandler(IIngredientRepository ingredients, IStockMovementRepository movements)
    : ICommandHandler<WriteOffStockCommand, Guid>
{
    public async Task<Guid> Handle(WriteOffStockCommand command, CancellationToken ct)
    {
        var ingredient = await ingredients.GetByIdAsync(command.IngredientId, ct)
            ?? throw new NotFoundException("Продукт не найден.");

        var movement = ingredient.WriteOff(command.Date, command.Quantity, command.Note);
        movements.Add(movement);
        return movement.Id;
    }
}

// ---------- Удаление записи журнала ----------

public sealed record DeleteStockMovementCommand(Guid Id) : ICommand<Unit>;

public sealed class DeleteStockMovementHandler(IIngredientRepository ingredients, IStockMovementRepository movements)
    : ICommandHandler<DeleteStockMovementCommand, Unit>
{
    public async Task<Unit> Handle(DeleteStockMovementCommand command, CancellationToken ct)
    {
        var movement = await movements.GetByIdAsync(command.Id, ct)
            ?? throw new NotFoundException("Запись журнала не найдена.");

        if (movement.Type == StockMovementType.Production)
            throw new DomainException("Расход на производство нельзя удалить отдельно: удалите партию в таблице дня.");

        var ingredient = await ingredients.GetByIdAsync(movement.IngredientId, ct)
            ?? throw new NotFoundException("Продукт не найден.");

        ingredient.RevertMovement(movement);
        movements.Remove(movement);
        return Unit.Value;
    }
}
