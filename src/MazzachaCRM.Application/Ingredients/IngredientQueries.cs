using MazzachaCRM.Application.Abstractions;
using MazzachaCRM.Application.Common;
using MazzachaCRM.Domain.Ingredients;
using Microsoft.EntityFrameworkCore;

namespace MazzachaCRM.Application.Ingredients;

public sealed record IngredientDto(
    Guid Id,
    string Name,
    IngredientKind Kind,
    MeasureUnit Unit,
    decimal PackagePrice,
    decimal PackageSize,
    decimal UnitPrice,
    decimal StockQuantity,
    decimal? MinStock,
    bool IsLowStock,
    bool IsOutOfStock,
    decimal StockValue,
    int UsedInProducts);

public sealed record GetIngredientsQuery : IQuery<IReadOnlyList<IngredientDto>>;

public sealed class GetIngredientsHandler(IReadDbContext db)
    : IQueryHandler<GetIngredientsQuery, IReadOnlyList<IngredientDto>>
{
    public async Task<IReadOnlyList<IngredientDto>> Handle(GetIngredientsQuery query, CancellationToken ct)
    {
        var ingredients = await db.Ingredients.OrderBy(i => i.Name).ToListAsync(ct);

        var usage = (await db.Products
                .SelectMany(p => p.Lines)
                .Where(l => l.IngredientId != null)
                .Select(l => l.IngredientId!.Value)
                .ToListAsync(ct))
            .GroupBy(id => id)
            .ToDictionary(g => g.Key, g => g.Count());

        return ingredients
            .Select(i => new IngredientDto(
                i.Id,
                i.Name,
                i.Kind,
                i.Unit,
                i.PackagePrice.Precise(),
                i.PackageSize.Quantity(),
                Math.Round(i.UnitPrice, 6),
                i.StockQuantity.Quantity(),
                i.MinStock,
                i.IsLowStock,
                i.IsOutOfStock,
                (Math.Max(i.StockQuantity, 0) * i.UnitPrice).Money(),
                usage.GetValueOrDefault(i.Id)))
            .ToList();
    }
}
