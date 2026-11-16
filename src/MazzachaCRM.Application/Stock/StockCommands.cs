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

// ---------- Общая закупка: несколько продуктов за один раз ----------

/// <summary>
/// Строка закупки. Либо существующий продукт (<paramref name="IngredientId"/>),
/// либо новый: тогда нужны название, тип и единица, а цена берётся из самой закупки.
/// </summary>
public sealed record PurchaseLineInput(
    Guid? IngredientId,
    string? NewName,
    IngredientKind? NewKind,
    MeasureUnit? NewUnit,
    decimal Quantity,
    decimal TotalCost,
    bool UpdatePrice = true);

public sealed record RegisterPurchasesCommand(DateOnly Date, IReadOnlyList<PurchaseLineInput> Lines, string? Note)
    : ICommand<int>;

public sealed class RegisterPurchasesValidator : AbstractValidator<RegisterPurchasesCommand>
{
    public RegisterPurchasesValidator()
    {
        RuleFor(x => x.Lines).NotEmpty().WithMessage("Добавьте хотя бы одну строку закупки.");
        RuleFor(x => x.Note).MaximumLength(500).WithName("Комментарий");
        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.Quantity).GreaterThan(0).WithName("Количество");
            line.RuleFor(l => l.TotalCost).GreaterThanOrEqualTo(0).WithName("Сумма");
            line.RuleFor(l => l.NewName)
                .NotEmpty()
                .When(l => l.IngredientId is null)
                .WithMessage("Укажите продукт.");
            line.RuleFor(l => l.NewName).MaximumLength(200).WithName("Название");
            line.RuleFor(l => l.NewUnit)
                .NotNull()
                .When(l => l.IngredientId is null)
                .WithMessage("Для нового продукта выберите единицу измерения.");
        });
    }
}

/// <summary>
/// Записывает закупку по всем строкам одной операцией. Новые продукты создаются на лету:
/// размер упаковки — килограмм, литр или штука, цена — по этой закупке.
/// </summary>
public sealed class RegisterPurchasesHandler(IIngredientRepository ingredients, IStockMovementRepository movements)
    : ICommandHandler<RegisterPurchasesCommand, int>
{
    public async Task<int> Handle(RegisterPurchasesCommand command, CancellationToken ct)
    {
        var all = await ingredients.ListAsync(ct);
        var byId = all.ToDictionary(i => i.Id);
        var byName = all.ToDictionary(i => i.Name.Trim(), StringComparer.CurrentCultureIgnoreCase);

        foreach (var line in command.Lines)
        {
            Ingredient ingredient;
            var updatePrice = line.UpdatePrice;

            if (line.IngredientId is { } id)
            {
                ingredient = byId.GetValueOrDefault(id) ?? throw new NotFoundException("Продукт не найден.");
            }
            else if (byName.TryGetValue(line.NewName!.Trim(), out var existing))
            {
                // Название совпало с уже существующим продуктом: это закупка его же.
                ingredient = existing;
            }
            else
            {
                var unit = line.NewUnit!.Value;
                var packageSize = unit == MeasureUnit.Piece ? 1m : 1000m;
                ingredient = Ingredient.Create(
                    line.NewName!,
                    line.NewKind ?? IngredientKind.Ingredient,
                    unit,
                    line.TotalCost / line.Quantity * packageSize,
                    packageSize);
                ingredients.Add(ingredient);
                byName[ingredient.Name] = ingredient;
                updatePrice = true;
            }

            movements.Add(ingredient.RegisterPurchase(command.Date, line.Quantity, line.TotalCost, updatePrice, command.Note));
        }

        return command.Lines.Count;
    }
}

// ---------- Обнуление остатков ----------

/// <summary>Ставит ноль на складе всем продуктам, в том числе ушедшим в минус. Возвращает число изменённых.</summary>
public sealed record ResetStockCommand(DateOnly Date) : ICommand<int>;

public sealed class ResetStockHandler(IIngredientRepository ingredients, IStockMovementRepository movements)
    : ICommandHandler<ResetStockCommand, int>
{
    public async Task<int> Handle(ResetStockCommand command, CancellationToken ct)
    {
        var changed = 0;
        foreach (var ingredient in await ingredients.ListAsync(ct))
        {
            if (ingredient.StockQuantity == 0)
                continue;
            movements.Add(ingredient.AdjustStockTo(command.Date, 0, "Обнуление остатков"));
            changed++;
        }

        return changed;
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
