using FluentValidation;
using MazzachaCRM.Application.Abstractions;
using MazzachaCRM.Application.Common;
using MazzachaCRM.Domain.Common;
using MazzachaCRM.Domain.Ingredients;
using MazzachaCRM.Domain.Products;

namespace MazzachaCRM.Application.Ingredients;

// ---------- Создание ----------

public sealed record CreateIngredientCommand(
    string Name,
    IngredientKind Kind,
    MeasureUnit Unit,
    decimal PackagePrice,
    decimal PackageSize,
    decimal? MinStock,
    decimal? InitialStock) : ICommand<Guid>;

public sealed class CreateIngredientValidator : AbstractValidator<CreateIngredientCommand>
{
    public CreateIngredientValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200).WithName("Название");
        RuleFor(x => x.Kind).IsInEnum().WithName("Тип");
        RuleFor(x => x.Unit).IsInEnum().WithName("Единица");
        RuleFor(x => x.PackagePrice).GreaterThanOrEqualTo(0).WithName("Цена");
        RuleFor(x => x.PackageSize).GreaterThan(0).WithName("Размер упаковки");
        RuleFor(x => x.MinStock).GreaterThanOrEqualTo(0).WithName("Минимальный остаток");
        RuleFor(x => x.InitialStock).GreaterThanOrEqualTo(0).WithName("Начальный остаток");
    }
}

public sealed class CreateIngredientHandler(IIngredientRepository ingredients, IStockMovementRepository movements)
    : ICommandHandler<CreateIngredientCommand, Guid>
{
    public async Task<Guid> Handle(CreateIngredientCommand command, CancellationToken ct)
    {
        if (await ingredients.NameExistsAsync(command.Name.Trim(), null, ct))
            throw new DomainException($"Продукт «{command.Name.Trim()}» уже есть в списке.");

        var ingredient = Ingredient.Create(
            command.Name, command.Kind, command.Unit, command.PackagePrice, command.PackageSize, command.MinStock);
        ingredients.Add(ingredient);

        if (command.InitialStock is > 0)
        {
            var today = DateOnly.FromDateTime(DateTime.Now);
            movements.Add(ingredient.AdjustStockTo(today, command.InitialStock.Value, "Начальный остаток"));
        }

        return ingredient.Id;
    }
}

// ---------- Изменение ----------

public sealed record UpdateIngredientCommand(
    Guid Id,
    string Name,
    IngredientKind Kind,
    MeasureUnit Unit,
    decimal PackagePrice,
    decimal PackageSize,
    decimal? MinStock) : ICommand<Unit>;

public sealed class UpdateIngredientValidator : AbstractValidator<UpdateIngredientCommand>
{
    public UpdateIngredientValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200).WithName("Название");
        RuleFor(x => x.Kind).IsInEnum().WithName("Тип");
        RuleFor(x => x.Unit).IsInEnum().WithName("Единица");
        RuleFor(x => x.PackagePrice).GreaterThanOrEqualTo(0).WithName("Цена");
        RuleFor(x => x.PackageSize).GreaterThan(0).WithName("Размер упаковки");
        RuleFor(x => x.MinStock).GreaterThanOrEqualTo(0).WithName("Минимальный остаток");
    }
}

public sealed class UpdateIngredientHandler(
    IIngredientRepository ingredients,
    IProductRepository products,
    IStockMovementRepository movements)
    : ICommandHandler<UpdateIngredientCommand, Unit>
{
    public async Task<Unit> Handle(UpdateIngredientCommand command, CancellationToken ct)
    {
        var ingredient = await ingredients.GetByIdAsync(command.Id, ct)
            ?? throw new NotFoundException("Продукт не найден.");

        if (await ingredients.NameExistsAsync(command.Name.Trim(), ingredient.Id, ct))
            throw new DomainException($"Продукт «{command.Name.Trim()}» уже есть в списке.");

        if (command.Unit != ingredient.Unit)
        {
            var usedIn = await products.ListNamesUsingIngredientAsync(ingredient.Id, ct);
            if (usedIn.Count > 0 || await movements.AnyForIngredientAsync(ingredient.Id, ct))
                throw new DomainException(
                    "Единицу измерения нельзя изменить: продукт уже используется в составах или на складе. Создайте новый продукт.");
            ingredient.ChangeUnit(command.Unit);
        }

        ingredient.Update(command.Name, command.Kind, command.PackagePrice, command.PackageSize, command.MinStock);
        return Unit.Value;
    }
}

// ---------- Удаление ----------

public sealed record DeleteIngredientCommand(Guid Id) : ICommand<Unit>;

public sealed class DeleteIngredientHandler(IIngredientRepository ingredients, IProductRepository products)
    : ICommandHandler<DeleteIngredientCommand, Unit>
{
    public async Task<Unit> Handle(DeleteIngredientCommand command, CancellationToken ct)
    {
        var ingredient = await ingredients.GetByIdAsync(command.Id, ct)
            ?? throw new NotFoundException("Продукт не найден.");

        var usedIn = await products.ListNamesUsingIngredientAsync(ingredient.Id, ct);
        if (usedIn.Count > 0)
            throw new DomainException(
                $"Продукт «{ingredient.Name}» используется в составе: {string.Join(", ", usedIn)}. Сначала уберите его оттуда.");

        ingredients.Remove(ingredient);
        return Unit.Value;
    }
}
