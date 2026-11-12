using MazzachaCRM.Application.Abstractions;
using MazzachaCRM.Application.Common;
using MazzachaCRM.Domain.Common;
using MazzachaCRM.Domain.Products;

namespace MazzachaCRM.Application.Reports;

public sealed record MarginRowDto(
    Guid ProductId,
    string Name,
    decimal UnitCost,
    CostDto UnitCostBreakdown,
    decimal SalePrice,
    decimal ProfitPerUnit,
    decimal? MarkupPercent,
    decimal? MarginPercent,
    decimal YieldQuantity,
    decimal BatchCost,
    int? BreakEvenUnits);

public sealed record GetMarginReportQuery : IQuery<IReadOnlyList<MarginRowDto>>;

/// <summary>Маржинальность готовых изделий по текущим ценам сырья и ценам продажи.</summary>
public sealed class GetMarginReportHandler(CostingContextLoader costing)
    : IQueryHandler<GetMarginReportQuery, IReadOnlyList<MarginRowDto>>
{
    public async Task<IReadOnlyList<MarginRowDto>> Handle(GetMarginReportQuery query, CancellationToken ct)
    {
        var context = await costing.LoadReadOnlyAsync(ct);
        var rows = new List<MarginRowDto>();

        foreach (var product in context.Products.Values.Where(p => p is { Type: ProductType.Finished, IsArchived: false }))
        {
            CostBreakdown unitCost;
            try
            {
                unitCost = context.Calculator.UnitCost(product.Id);
            }
            catch (DomainException)
            {
                continue;
            }

            var batchCost = unitCost.Total * product.YieldQuantity;
            rows.Add(new MarginRowDto(
                product.Id,
                product.Name,
                unitCost.Total.Precise(),
                CostDto.From(unitCost),
                product.SalePrice.Precise(),
                (product.SalePrice - unitCost.Total).Precise(),
                Rounding.MarkupPercent(product.SalePrice, unitCost.Total),
                Rounding.MarginPercent(product.SalePrice, unitCost.Total),
                product.YieldQuantity,
                batchCost.Money(),
                Rounding.BreakEvenUnits(batchCost, product.SalePrice)));
        }

        return rows
            .OrderByDescending(r => r.MarkupPercent ?? decimal.MinValue)
            .ThenBy(r => r.Name)
            .ToList();
    }
}
