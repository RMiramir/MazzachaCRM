using MazzachaCRM.Application.Abstractions;
using MazzachaCRM.Application.Common;
using MazzachaCRM.Domain.Common;
using MazzachaCRM.Domain.Ingredients;
using MazzachaCRM.Domain.Products;

namespace MazzachaCRM.Application.Products;

public sealed record ProductListItemDto(
    Guid Id,
    string Name,
    ProductType Type,
    decimal YieldQuantity,
    MeasureUnit YieldUnit,
    decimal LaborMinutes,
    decimal SalePrice,
    bool IsArchived,
    CostDto? UnitCost,
    decimal? BatchCost,
    decimal? ProfitPerUnit,
    decimal? MarkupPercent,
    string? CostError);

public sealed record RecipeLineDto(
    Guid? IngredientId,
    Guid? ComponentProductId,
    string Name,
    MeasureUnit Unit,
    bool IsPackaging,
    decimal Quantity,
    decimal Cost);

public sealed record ProductDetailDto(
    Guid Id,
    string Name,
    ProductType Type,
    decimal YieldQuantity,
    MeasureUnit YieldUnit,
    decimal LaborMinutes,
    decimal SalePrice,
    string? Note,
    bool IsArchived,
    IReadOnlyList<RecipeLineDto> Lines,
    CostDto BatchCost,
    CostDto UnitCost);

public sealed record CostPreviewDto(
    IReadOnlyList<decimal> LineCosts,
    CostDto BatchCost,
    CostDto UnitCost,
    decimal? ProfitPerUnit,
    decimal? MarkupPercent,
    int? BreakEvenUnits);

// ---------- Список ----------

public sealed record GetProductsQuery(bool IncludeArchived) : IQuery<IReadOnlyList<ProductListItemDto>>;

public sealed class GetProductsHandler(CostingContextLoader costing)
    : IQueryHandler<GetProductsQuery, IReadOnlyList<ProductListItemDto>>
{
    public async Task<IReadOnlyList<ProductListItemDto>> Handle(GetProductsQuery query, CancellationToken ct)
    {
        var context = await costing.LoadReadOnlyAsync(ct);

        return context.Products.Values
            .Where(p => query.IncludeArchived || !p.IsArchived)
            .OrderBy(p => p.Type)
            .ThenBy(p => p.Name)
            .Select(p => ToListItem(p, context.Calculator))
            .ToList();
    }

    private static ProductListItemDto ToListItem(Product product, ProductCostCalculator calculator)
    {
        CostBreakdown? unitCost = null;
        string? error = null;
        try
        {
            unitCost = calculator.UnitCost(product.Id);
        }
        catch (DomainException ex)
        {
            error = ex.Message;
        }

        var isSold = product.Type == ProductType.Finished && unitCost is not null;
        return new ProductListItemDto(
            product.Id,
            product.Name,
            product.Type,
            product.YieldQuantity,
            product.YieldUnit,
            product.LaborMinutes,
            product.SalePrice.Precise(),
            product.IsArchived,
            unitCost is { } cost ? CostDto.From(cost) : null,
            unitCost is { } c ? (c.Total * product.YieldQuantity).Money() : null,
            isSold ? (product.SalePrice - unitCost!.Value.Total).Precise() : null,
            isSold ? Rounding.MarkupPercent(product.SalePrice, unitCost!.Value.Total) : null,
            error);
    }
}

// ---------- Карточка ----------

public sealed record GetProductQuery(Guid Id) : IQuery<ProductDetailDto>;

public sealed class GetProductHandler(CostingContextLoader costing)
    : IQueryHandler<GetProductQuery, ProductDetailDto>
{
    public async Task<ProductDetailDto> Handle(GetProductQuery query, CancellationToken ct)
    {
        var context = await costing.LoadReadOnlyAsync(ct);
        if (!context.Products.TryGetValue(query.Id, out var product))
            throw new NotFoundException("Изделие не найдено.");

        var calculator = context.Calculator;
        var lines = product.Lines
            .Select(line =>
            {
                var spec = new RecipeLineSpec(line.IngredientId, line.ComponentProductId, line.Quantity);
                var cost = calculator.LineCost(spec, product.Id).Total.Precise();

                if (line.IngredientId is { } ingredientId)
                {
                    var ingredient = context.Ingredients[ingredientId];
                    return new RecipeLineDto(
                        ingredientId, null, ingredient.Name, ingredient.Unit,
                        ingredient.Kind == IngredientKind.Packaging, line.Quantity, cost);
                }

                var component = context.Products[line.ComponentProductId!.Value];
                return new RecipeLineDto(
                    null, component.Id, component.Name, component.YieldUnit, false, line.Quantity, cost);
            })
            .ToList();

        var batchCost = calculator.BatchCost(product.Id);
        return new ProductDetailDto(
            product.Id,
            product.Name,
            product.Type,
            product.YieldQuantity,
            product.YieldUnit,
            product.LaborMinutes,
            product.SalePrice.Precise(),
            product.Note,
            product.IsArchived,
            lines,
            CostDto.From(batchCost),
            CostDto.From(batchCost / product.YieldQuantity));
    }
}

// ---------- Живой пересчёт в редакторе ----------

public sealed record PreviewProductCostQuery(
    Guid? ProductId,
    decimal YieldQuantity,
    decimal LaborMinutes,
    decimal SalePrice,
    IReadOnlyList<RecipeLineInput> Lines) : IQuery<CostPreviewDto>;

public sealed class PreviewProductCostHandler(CostingContextLoader costing)
    : IQueryHandler<PreviewProductCostQuery, CostPreviewDto>
{
    public async Task<CostPreviewDto> Handle(PreviewProductCostQuery query, CancellationToken ct)
    {
        var context = await costing.LoadReadOnlyAsync(ct);
        var calculator = context.Calculator;

        // Черновик может быть заполнен не до конца: незавершённые строки стоят ноль.
        var complete = (query.Lines ?? [])
            .Select(l => l.Quantity > 0 && (l.IngredientId is null) != (l.ComponentProductId is null)
                ? l.ToSpec()
                : (RecipeLineSpec?)null)
            .ToList();

        var lineCosts = complete
            .Select(spec => spec is { } s ? calculator.LineCost(s, query.ProductId).Total.Precise() : 0m)
            .ToList();

        var batchCost = calculator.DraftBatchCost(
            query.ProductId,
            complete.Where(s => s is not null).Select(s => s!.Value),
            Math.Max(query.LaborMinutes, 0));

        var yield = query.YieldQuantity > 0 ? query.YieldQuantity : 1;
        var unitCost = batchCost / yield;

        return new CostPreviewDto(
            lineCosts,
            CostDto.From(batchCost),
            CostDto.From(unitCost),
            query.SalePrice > 0 ? (query.SalePrice - unitCost.Total).Precise() : null,
            query.SalePrice > 0 ? Rounding.MarkupPercent(query.SalePrice, unitCost.Total) : null,
            Rounding.BreakEvenUnits(batchCost.Total, query.SalePrice));
    }
}
