using FluentValidation;
using MazzachaCRM.Application.Abstractions;
using MazzachaCRM.Application.Common;
using MazzachaCRM.Domain.Common;
using MazzachaCRM.Domain.Ingredients;
using MazzachaCRM.Domain.Orders;
using MazzachaCRM.Domain.Production;
using MazzachaCRM.Domain.Products;

namespace MazzachaCRM.Application.Products;

public sealed record RecipeLineInput(Guid? IngredientId, Guid? ComponentProductId, decimal Quantity)
{
    public RecipeLineSpec ToSpec() => new(IngredientId, ComponentProductId, Quantity);
}

// ---------- Создание ----------

public sealed record CreateProductCommand(
    string Name,
    ProductType Type,
    decimal YieldQuantity,
    MeasureUnit YieldUnit,
    decimal LaborMinutes,
    decimal SalePrice,
    string? Note,
    IReadOnlyList<RecipeLineInput> Lines) : ICommand<Guid>;

public sealed class CreateProductValidator : AbstractValidator<CreateProductCommand>
{
    public CreateProductValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200).WithName("Название");
        RuleFor(x => x.Type).IsInEnum().WithName("Тип");
        RuleFor(x => x.YieldUnit).IsInEnum().WithName("Единица выхода");
        RuleFor(x => x.YieldQuantity).GreaterThan(0).WithName("Выход партии");
        RuleFor(x => x.LaborMinutes).GreaterThanOrEqualTo(0).WithName("Время работы");
        RuleFor(x => x.SalePrice).GreaterThanOrEqualTo(0).WithName("Цена продажи");
        RuleFor(x => x.Note).MaximumLength(1000).WithName("Заметка");
        RuleFor(x => x.Lines).NotEmpty().WithMessage("Добавьте в состав хотя бы один продукт.");
        RuleForEach(x => x.Lines).ChildRules(line =>
            line.RuleFor(l => l.Quantity).GreaterThan(0).WithName("Количество в составе"));
    }
}

public sealed class CreateProductHandler(IProductRepository products, CostingContextLoader costing)
    : ICommandHandler<CreateProductCommand, Guid>
{
    public async Task<Guid> Handle(CreateProductCommand command, CancellationToken ct)
    {
        if (await products.NameExistsAsync(command.Name.Trim(), null, ct))
            throw new DomainException($"Изделие «{command.Name.Trim()}» уже есть в списке.");

        var product = Product.Create(
            command.Name,
            command.Type,
            command.YieldQuantity,
            command.YieldUnit,
            command.LaborMinutes,
            command.SalePrice,
            command.Note,
            command.Lines.Select(l => l.ToSpec()));

        // Проверяет, что все компоненты существуют и состав не зациклен.
        var context = await costing.LoadTrackedAsync(ct);
        context.CalculatorWith(product).BatchCost(product.Id);

        products.Add(product);
        return product.Id;
    }
}

// ---------- Изменение ----------

public sealed record UpdateProductCommand(
    Guid Id,
    string Name,
    ProductType Type,
    decimal YieldQuantity,
    MeasureUnit YieldUnit,
    decimal LaborMinutes,
    decimal SalePrice,
    string? Note,
    IReadOnlyList<RecipeLineInput> Lines) : ICommand<Unit>;

public sealed class UpdateProductValidator : AbstractValidator<UpdateProductCommand>
{
    public UpdateProductValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200).WithName("Название");
        RuleFor(x => x.Type).IsInEnum().WithName("Тип");
        RuleFor(x => x.YieldUnit).IsInEnum().WithName("Единица выхода");
        RuleFor(x => x.YieldQuantity).GreaterThan(0).WithName("Выход партии");
        RuleFor(x => x.LaborMinutes).GreaterThanOrEqualTo(0).WithName("Время работы");
        RuleFor(x => x.SalePrice).GreaterThanOrEqualTo(0).WithName("Цена продажи");
        RuleFor(x => x.Note).MaximumLength(1000).WithName("Заметка");
        RuleFor(x => x.Lines).NotEmpty().WithMessage("Добавьте в состав хотя бы один продукт.");
        RuleForEach(x => x.Lines).ChildRules(line =>
            line.RuleFor(l => l.Quantity).GreaterThan(0).WithName("Количество в составе"));
    }
}

public sealed class UpdateProductHandler(
    IProductRepository products,
    IProductionBatchRepository batches,
    IOrderRepository orders,
    CostingContextLoader costing)
    : ICommandHandler<UpdateProductCommand, Unit>
{
    public async Task<Unit> Handle(UpdateProductCommand command, CancellationToken ct)
    {
        var product = await products.GetByIdAsync(command.Id, ct)
            ?? throw new NotFoundException("Изделие не найдено.");

        if (await products.NameExistsAsync(command.Name.Trim(), product.Id, ct))
            throw new DomainException($"Изделие «{command.Name.Trim()}» уже есть в списке.");

        if (product.Type == ProductType.Finished && command.Type == ProductType.SemiFinished &&
            (await batches.AnyForProductAsync(product.Id, ct) || await orders.AnyForProductAsync(product.Id, ct)))
        {
            throw new DomainException(
                "Изделие уже продавалось или есть в заказах, поэтому его нельзя превратить в полуфабрикат.");
        }

        product.Update(
            command.Name,
            command.Type,
            command.YieldQuantity,
            command.YieldUnit,
            command.LaborMinutes,
            command.SalePrice,
            command.Note,
            command.Lines.Select(l => l.ToSpec()));

        var context = await costing.LoadTrackedAsync(ct);
        var calculator = context.CalculatorWith(product);
        calculator.BatchCost(product.Id);

        // Изделия, в состав которых входит это, тоже не должны зациклиться.
        foreach (var other in context.Products.Values.Where(p => p.Id != product.Id))
            calculator.BatchCost(other.Id);

        return Unit.Value;
    }
}

// ---------- Удаление и архив ----------

public sealed record DeleteProductCommand(Guid Id) : ICommand<Unit>;

public sealed class DeleteProductHandler(
    IProductRepository products,
    IProductionBatchRepository batches,
    IOrderRepository orders)
    : ICommandHandler<DeleteProductCommand, Unit>
{
    public async Task<Unit> Handle(DeleteProductCommand command, CancellationToken ct)
    {
        var product = await products.GetByIdAsync(command.Id, ct)
            ?? throw new NotFoundException("Изделие не найдено.");

        var usedIn = await products.ListNamesUsingProductAsync(product.Id, ct);
        if (usedIn.Count > 0)
            throw new DomainException(
                $"«{product.Name}» входит в состав: {string.Join(", ", usedIn)}. Сначала уберите его оттуда.");

        if (await batches.AnyForProductAsync(product.Id, ct) || await orders.AnyForProductAsync(product.Id, ct))
            throw new DomainException(
                $"«{product.Name}» уже есть в записях дней или в заказах, поэтому удалить его нельзя. Отправьте изделие в архив.");

        products.Remove(product);
        return Unit.Value;
    }
}

public sealed record SetProductArchivedCommand(Guid Id, bool Archived) : ICommand<Unit>;

public sealed class SetProductArchivedHandler(IProductRepository products)
    : ICommandHandler<SetProductArchivedCommand, Unit>
{
    public async Task<Unit> Handle(SetProductArchivedCommand command, CancellationToken ct)
    {
        var product = await products.GetByIdAsync(command.Id, ct)
            ?? throw new NotFoundException("Изделие не найдено.");

        if (command.Archived)
            product.Archive();
        else
            product.Restore();
        return Unit.Value;
    }
}
