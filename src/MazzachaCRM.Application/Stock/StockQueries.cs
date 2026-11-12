using MazzachaCRM.Application.Abstractions;
using MazzachaCRM.Application.Common;
using MazzachaCRM.Domain.Ingredients;
using Microsoft.EntityFrameworkCore;

namespace MazzachaCRM.Application.Stock;

public sealed record StockMovementDto(
    Guid Id,
    DateOnly Date,
    Guid IngredientId,
    string IngredientName,
    MeasureUnit Unit,
    StockMovementType Type,
    decimal Quantity,
    decimal? TotalCost,
    string? Note,
    bool CanDelete);

public sealed record StockMovementsDto(IReadOnlyList<StockMovementDto> Items, decimal PurchasesTotal);

public sealed record GetStockMovementsQuery(Guid? IngredientId, DateOnly? From, DateOnly? To, int Take = 300)
    : IQuery<StockMovementsDto>;

public sealed class GetStockMovementsHandler(IReadDbContext db)
    : IQueryHandler<GetStockMovementsQuery, StockMovementsDto>
{
    public async Task<StockMovementsDto> Handle(GetStockMovementsQuery query, CancellationToken ct)
    {
        var movements = db.StockMovements;
        if (query.IngredientId is { } ingredientId)
            movements = movements.Where(m => m.IngredientId == ingredientId);
        if (query.From is { } from)
            movements = movements.Where(m => m.Date >= from);
        if (query.To is { } to)
            movements = movements.Where(m => m.Date <= to);

        var list = await movements
            .OrderByDescending(m => m.Date)
            .ThenByDescending(m => m.CreatedAt)
            .Take(Math.Clamp(query.Take, 1, 1000))
            .ToListAsync(ct);

        var ingredients = await db.Ingredients.ToDictionaryAsync(i => i.Id, ct);

        var items = list
            .Where(m => ingredients.ContainsKey(m.IngredientId))
            .Select(m =>
            {
                var ingredient = ingredients[m.IngredientId];
                return new StockMovementDto(
                    m.Id,
                    m.Date,
                    m.IngredientId,
                    ingredient.Name,
                    ingredient.Unit,
                    m.Type,
                    m.Quantity.Quantity(),
                    m.TotalCost?.Money(),
                    m.Note,
                    m.Type != StockMovementType.Production);
            })
            .ToList();

        var purchasesTotal = list.Where(m => m.Type == StockMovementType.Purchase).Sum(m => m.TotalCost ?? 0);
        return new StockMovementsDto(items, purchasesTotal.Money());
    }
}
