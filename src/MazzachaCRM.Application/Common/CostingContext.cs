using MazzachaCRM.Application.Abstractions;
using MazzachaCRM.Domain.Ingredients;
using MazzachaCRM.Domain.Products;
using MazzachaCRM.Domain.Settings;
using Microsoft.EntityFrameworkCore;

namespace MazzachaCRM.Application.Common;

/// <summary>Всё, что нужно для расчёта себестоимости: изделия с составами, продукты и ставка за час.</summary>
public sealed record CostingContext(
    IReadOnlyDictionary<Guid, Product> Products,
    IReadOnlyDictionary<Guid, Ingredient> Ingredients,
    AppSettings Settings)
{
    public ProductCostCalculator Calculator { get; } = new(Products, Ingredients, Settings.HourlyRate);

    /// <summary>Калькулятор, который видит ещё не сохранённое изделие (при создании и изменении).</summary>
    public ProductCostCalculator CalculatorWith(Product product)
    {
        var products = Products.Values.Where(p => p.Id != product.Id).Append(product).ToDictionary(p => p.Id);
        return new ProductCostCalculator(products, Ingredients, Settings.HourlyRate);
    }
}

public sealed class CostingContextLoader(
    IProductRepository products,
    IIngredientRepository ingredients,
    ISettingsRepository settings,
    IReadDbContext db)
{
    /// <summary>Для команд: сущности отслеживаются, их можно менять (например, списывать сырьё).</summary>
    public async Task<CostingContext> LoadTrackedAsync(CancellationToken ct)
    {
        var productList = await products.ListAsync(ct);
        var ingredientList = await ingredients.ListAsync(ct);
        var appSettings = await settings.GetAsync(ct);
        return new CostingContext(
            productList.ToDictionary(p => p.Id),
            ingredientList.ToDictionary(i => i.Id),
            appSettings);
    }

    /// <summary>Для запросов: только чтение.</summary>
    public async Task<CostingContext> LoadReadOnlyAsync(CancellationToken ct)
    {
        var productList = await db.Products.Include(p => p.Lines).ToListAsync(ct);
        var ingredientList = await db.Ingredients.ToListAsync(ct);
        var appSettings = await db.Settings.FirstOrDefaultAsync(ct) ?? AppSettings.CreateDefault();
        return new CostingContext(
            productList.ToDictionary(p => p.Id),
            ingredientList.ToDictionary(i => i.Id),
            appSettings);
    }
}
