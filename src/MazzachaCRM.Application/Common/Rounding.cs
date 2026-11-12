using MazzachaCRM.Domain.Products;

namespace MazzachaCRM.Application.Common;

/// <summary>
/// Округление только на выходе в DTO. Внутри расчётов значения не округляются,
/// чтобы 12 кусков по 52/12 снова давали ровно 52.
/// </summary>
public static class Rounding
{
    /// <summary>Итоговые суммы: два знака.</summary>
    public static decimal Money(this decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    /// <summary>Цены и себестоимость за единицу: четыре знака.</summary>
    public static decimal Precise(this decimal value) => Math.Round(value, 4, MidpointRounding.AwayFromZero);

    /// <summary>Количества: три знака.</summary>
    public static decimal Quantity(this decimal value) => Math.Round(value, 3, MidpointRounding.AwayFromZero);

    /// <summary>Наценка в процентах от себестоимости.</summary>
    public static decimal? MarkupPercent(decimal price, decimal cost) =>
        cost <= 0 ? null : Math.Round((price - cost) / cost * 100, 1, MidpointRounding.AwayFromZero);

    /// <summary>Маржа в процентах от цены.</summary>
    public static decimal? MarginPercent(decimal price, decimal cost) =>
        price <= 0 ? null : Math.Round((price - cost) / price * 100, 1, MidpointRounding.AwayFromZero);

    /// <summary>Сколько штук нужно продать, чтобы покрыть затраты.</summary>
    public static int? BreakEvenUnits(decimal totalCost, decimal price) =>
        price <= 0 ? null : (int)Math.Ceiling(Math.Round(totalCost / price, 6));
}

public sealed record CostDto(decimal Ingredients, decimal Packaging, decimal Labor, decimal Total)
{
    public static CostDto From(CostBreakdown cost) =>
        new(cost.Ingredients.Precise(), cost.Packaging.Precise(), cost.Labor.Precise(), cost.Total.Precise());
}
