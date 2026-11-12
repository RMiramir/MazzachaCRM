using MazzachaCRM.Domain.Common;
using MazzachaCRM.Domain.Ingredients;
using MazzachaCRM.Domain.Products;

namespace MazzachaCRM.Domain.Tests;

public class ProductCostCalculatorTests
{
    private readonly Ingredient _flour = Ingredient.Create("Мука", IngredientKind.Ingredient, MeasureUnit.Gram, 8, 1000);
    private readonly Ingredient _butter = Ingredient.Create("Масло", IngredientKind.Ingredient, MeasureUnit.Gram, 90, 1000);
    private readonly Ingredient _box = Ingredient.Create("Коробка", IngredientKind.Packaging, MeasureUnit.Piece, 40, 10);
    private readonly Ingredient _sliceBox = Ingredient.Create("Контейнер", IngredientKind.Packaging, MeasureUnit.Piece, 50, 100);

    private ProductCostCalculator Calculator(decimal hourlyRate, params Product[] products) =>
        new(
            products.ToDictionary(p => p.Id),
            new[] { _flour, _butter, _box, _sliceBox }.ToDictionary(i => i.Id),
            hourlyRate);

    private static RecipeLineSpec Raw(Ingredient ingredient, decimal quantity) => new(ingredient.Id, null, quantity);

    private static RecipeLineSpec Part(Product product, decimal quantity) => new(null, product.Id, quantity);

    private static Product Finished(string name, decimal yield, decimal laborMinutes, params RecipeLineSpec[] lines) =>
        Product.Create(name, ProductType.Finished, yield, MeasureUnit.Piece, laborMinutes, 10, null, lines);

    private static Product SemiFinished(string name, decimal yield, params RecipeLineSpec[] lines) =>
        Product.Create(name, ProductType.SemiFinished, yield, MeasureUnit.Piece, 0, 0, null, lines);

    [Fact]
    public void Unit_cost_is_batch_cost_divided_by_yield()
    {
        // 500 г муки по 0,008 = 4; 100 г масла по 0,09 = 9; партия 13, выход 10 шт.
        var product = Finished("Печенье", 10, 0, Raw(_flour, 500), Raw(_butter, 100));

        var calculator = Calculator(0, product);

        Assert.Equal(13m, calculator.BatchCost(product.Id).Total);
        Assert.Equal(1.3m, calculator.UnitCost(product.Id).Total);
    }

    [Fact]
    public void Packaging_and_labor_are_reported_separately()
    {
        // Сырьё 4, упаковка 2 × 4 = 8, работа 30 минут по 60 за час = 30.
        var product = Finished("Набор", 2, 30, Raw(_flour, 500), Raw(_box, 2));

        var cost = Calculator(60, product).BatchCost(product.Id);

        Assert.Equal(4m, cost.Ingredients);
        Assert.Equal(8m, cost.Packaging);
        Assert.Equal(30m, cost.Labor);
        Assert.Equal(42m, cost.Total);
    }

    [Fact]
    public void Semi_finished_cost_flows_into_products_that_use_it()
    {
        // Основа: 600 г муки (4,8) + 480 г масла (43,2) = 48 за один торт.
        var cakeBase = SemiFinished("Основа", 1, Raw(_flour, 600), Raw(_butter, 480));
        var whole = Finished("Целый торт", 1, 0, Part(cakeBase, 1), Raw(_box, 1));
        var slice = Finished("Кусок", 12, 0, Part(cakeBase, 1), Raw(_sliceBox, 12));

        var calculator = Calculator(0, cakeBase, whole, slice);

        Assert.Equal(52m, calculator.UnitCost(whole.Id).Total);
        Assert.Equal(4.5m, calculator.UnitCost(slice.Id).Total);
        Assert.Equal(0.5m, calculator.UnitCost(slice.Id).Packaging);
    }

    [Fact]
    public void Labor_of_semi_finished_is_counted_as_labor_of_the_product()
    {
        var cream = Product.Create(
            "Крем", ProductType.SemiFinished, 1000, MeasureUnit.Gram, 60, 0, null, [Raw(_butter, 1000)]);
        var cake = Finished("Торт", 1, 30, Part(cream, 500));

        var cost = Calculator(40, cream, cake).UnitCost(cake.Id);

        Assert.Equal(45m, cost.Ingredients); // половина крема: 500 г масла
        Assert.Equal(40m, cost.Labor);       // 20 за половину крема и 20 за сам торт
    }

    [Fact]
    public void Raw_requirements_expand_nested_products_down_to_ingredients()
    {
        var cakeBase = SemiFinished("Основа", 1, Raw(_flour, 600), Raw(_butter, 480));
        var slice = Finished("Кусок", 12, 0, Part(cakeBase, 1), Raw(_sliceBox, 12));

        // 24 куска — это две основы и 24 контейнера.
        var requirements = Calculator(0, cakeBase, slice).RawRequirements(slice.Id, 24);

        Assert.Equal(1200m, requirements[_flour.Id]);
        Assert.Equal(960m, requirements[_butter.Id]);
        Assert.Equal(24m, requirements[_sliceBox.Id]);
    }

    [Fact]
    public void Circular_composition_is_rejected()
    {
        var a = SemiFinished("А", 1, Raw(_flour, 100));
        var b = SemiFinished("Б", 1, Part(a, 1));
        a.Update("А", ProductType.SemiFinished, 1, MeasureUnit.Piece, 0, 0, null, [Part(b, 1)]);

        var calculator = Calculator(0, a, b);

        var error = Assert.Throws<DomainException>(() => calculator.BatchCost(a.Id));
        Assert.Contains("зациклен", error.Message);
    }

    [Fact]
    public void Draft_cost_detects_a_cycle_through_the_edited_product()
    {
        var a = SemiFinished("А", 1, Raw(_flour, 100));
        var b = SemiFinished("Б", 1, Part(a, 1));

        // В черновике изделия «А» пытаются добавить «Б», которое само состоит из «А».
        var calculator = Calculator(0, a, b);

        Assert.Throws<DomainException>(() => calculator.DraftBatchCost(a.Id, [Part(b, 1)], 0));
    }

    [Fact]
    public void Product_cannot_contain_itself_or_duplicate_components()
    {
        var product = Finished("Торт", 1, 0, Raw(_flour, 100));

        Assert.Throws<DomainException>(() => product.Update(
            "Торт", ProductType.Finished, 1, MeasureUnit.Piece, 0, 10, null, [Part(product, 1)]));
        Assert.Throws<DomainException>(() => product.Update(
            "Торт", ProductType.Finished, 1, MeasureUnit.Piece, 0, 10, null, [Raw(_flour, 100), Raw(_flour, 50)]));
    }
}
