using MazzachaCRM.Application.Abstractions;
using MazzachaCRM.Application.Customers;
using MazzachaCRM.Application.Ingredients;
using MazzachaCRM.Application.Orders;
using MazzachaCRM.Application.Production;
using MazzachaCRM.Application.Products;
using MazzachaCRM.Application.Stock;
using MazzachaCRM.Domain.Ingredients;
using MazzachaCRM.Domain.Orders;
using MazzachaCRM.Domain.Products;
using Microsoft.EntityFrameworkCore;

namespace MazzachaCRM.Infrastructure.Persistence;

/// <summary>
/// Демо-данные: три дня продаж из исходного расчёта владельца.
/// Составы подобраны так, чтобы себестоимость совпала с его цифрами
/// (чизкейк 5, торт в формочке 7,2, трайфл 13, маффин 1,3, медовый кусок 4,5, целый торт 52).
/// Всё создаётся обычными командами, поэтому заодно проверяет весь путь через приложение.
/// </summary>
public sealed class DemoDataSeeder(IDispatcher dispatcher, IReadDbContext db)
{
    /// <returns>false, если в базе уже есть данные и заполнение пропущено.</returns>
    public async Task<bool> SeedAsync(DateOnly lastDay, CancellationToken ct = default)
    {
        if (await db.Ingredients.AnyAsync(ct) || await db.Products.AnyAsync(ct))
            return false;

        var day1 = lastDay.AddDays(-2);
        var day2 = lastDay.AddDays(-1);
        var day3 = lastDay;
        var purchaseDay = day1.AddDays(-1);

        // ---------- Продукты и упаковка: цена за упаковку, закупка на склад ----------
        var flour = await Ingredient("Мука", MeasureUnit.Gram, 8, 1000, purchased: 5000);
        var sugar = await Ingredient("Сахар", MeasureUnit.Gram, 12, 1000, purchased: 3000);
        var eggs = await Ingredient("Яйцо", MeasureUnit.Piece, 15, 10, purchased: 30);
        var butter = await Ingredient("Масло сливочное", MeasureUnit.Gram, 90, 1000, purchased: 1000);
        var honey = await Ingredient("Мёд", MeasureUnit.Gram, 60, 1000, purchased: 1000);
        var sourCream = await Ingredient("Сметана", MeasureUnit.Gram, 30, 1000, purchased: 1500);
        var creamCheese = await Ingredient("Сливочный сыр", MeasureUnit.Gram, 120, 1000, purchased: 2000);
        var cream = await Ingredient("Сливки 33%", MeasureUnit.Milliliter, 80, 1000, purchased: 3000, minStock: 500);
        var cookies = await Ingredient("Печенье", MeasureUnit.Gram, 40, 1000, purchased: 2000);
        var chocolate = await Ingredient("Шоколад", MeasureUnit.Gram, 60, 1000, purchased: 1000);
        var milk = await Ingredient("Молоко", MeasureUnit.Milliliter, 10, 1000, purchased: 1000);

        var heartMold = await Packaging("Формочка «Сердце»", 60, 100, purchased: 100);
        var foilMold = await Packaging("Формочка алюминиевая", 80, 100, purchased: 100);
        var cup = await Packaging("Стакан для трайфла", 50, 50, purchased: 50, minStock: 20);
        var muffinCase = await Packaging("Капсула для маффина", 10, 100, purchased: 100);
        var cakeBox = await Packaging("Коробка для торта", 40, 10, purchased: 10);
        var sliceBox = await Packaging("Контейнер для куска", 50, 100, purchased: 100);

        // ---------- Изделия ----------
        var cheesecake = await Product("Мини-чизкейк «Сердце»", yield: 20, price: 7.5m,
            (creamCheese, 500), (cookies, 200), (butter, 80), (sugar, 150), (eggs, 2), (cream, 100), (heartMold, 20));

        var moldCake = await Product("Торт в формочке", yield: 10, price: 14,
            (flour, 325), (sugar, 200), (eggs, 4), (butter, 100), (cream, 400), (creamCheese, 100), (foilMold, 10));

        var trifle = await Product("Трайфл", yield: 4, price: 20,
            (cream, 300), (creamCheese, 100), (cookies, 150), (sugar, 50), (chocolate, 90), (cup, 4));

        var muffin = await Product("Маффин", yield: 23, price: 3.25m,
            (flour, 500), (sugar, 300), (eggs, 4), (butter, 100), (milk, 200), (chocolate, 50), (muffinCase, 23));

        // Полуфабрикат: один торт-основа, из которого получается либо целый торт, либо 12 кусков.
        var honeyBase = await dispatcher.Send(new CreateProductCommand(
            "Медовый торт (основа)", ProductType.SemiFinished, 1, MeasureUnit.Piece, 0, 0,
            "Коржи и крем без упаковки",
            [
                Line(flour, 600), Line(sugar, 300), Line(eggs, 4),
                Line(butter, 150), Line(honey, 150), Line(sourCream, 370),
            ]), ct);

        var honeyWhole = await dispatcher.Send(new CreateProductCommand(
            "Медовый торт (целый)", ProductType.Finished, 1, MeasureUnit.Piece, 0, 100, null,
            [new RecipeLineInput(null, honeyBase, 1), Line(cakeBox, 1)]), ct);

        var honeySlice = await dispatcher.Send(new CreateProductCommand(
            "Медовый торт (кусок)", ProductType.Finished, 12, MeasureUnit.Piece, 0, 9.5m, null,
            [new RecipeLineInput(null, honeyBase, 1), Line(sliceBox, 12)]), ct);

        // ---------- День 1 ----------
        var cheesecakeBatch = await Batch(day1, cheesecake, 20, 7.5m, sold: 12);
        await dispatcher.Send(new SetBatchWriteOffCommand(cheesecakeBatch, day1, 8, "Не продали"), ct);
        await Batch(day1, moldCake, 10, 14, sold: 10);
        await Batch(day1, trifle, 2, 20, sold: 2);

        // ---------- День 2 ----------
        await Batch(day2, honeySlice, 12, 8, sold: 12);
        await Batch(day2, muffin, 23, 3.25m, sold: 23);
        await Batch(day2, trifle, 2, 20, sold: 2);

        // ---------- День 3 ----------
        await Batch(day3, trifle, 28, 20, sold: 0);
        await Batch(day3, honeySlice, 12, 9.5m, sold: 3);
        await Batch(day3, honeyWhole, 1, 100, sold: 1);

        // ---------- Поставка в магазин: продали 20 из 23, 3 вернули и списали, магазин заплатил часть ----------
        var shop = await dispatcher.Send(new CreateCustomerCommand("Магазин у дома", null, "Платит после продажи", true), ct);
        var shipment = await dispatcher.Send(new CreateOrderCommand(
            shop, null, null, lastDay.AddDays(-4), null, 0, null,
            [new OrderItemInput(muffin, 23, 3.25m, null)]), ct);
        await dispatcher.Send(new ChangeOrderStatusCommand(shipment, OrderStatus.Delivered, lastDay.AddDays(-4)), ct);
        var shipmentItem = (await dispatcher.Query(new GetOrderQuery(shipment), ct)).Items[0].Id;
        await dispatcher.Send(new SubmitShopReportCommand(shipment, lastDay.AddDays(-3), [new ShopReportItemInput(shipmentItem, 20)]), ct);
        var returnedBatch = (await dispatcher.Query(new GetDayQuery(lastDay.AddDays(-3)), ct)).CarryOver.Single().Id;
        await dispatcher.Send(new SetBatchWriteOffCommand(returnedBatch, lastDay.AddDays(-3), 3, "Вернули из магазина"), ct);
        await dispatcher.Send(new AddOrderPaymentCommand(shipment, lastDay.AddDays(-3), 30, "Магазин, частично"), ct);

        // ---------- Предзаказ на завтра ----------
        await dispatcher.Send(new CreateOrderCommand(
            null, "Мадина", "+992 900 00 00 00", lastDay.AddDays(1), new TimeOnly(15, 0), 50,
            "Надпись «С днём рождения!»",
            [new OrderItemInput(honeyWhole, 1, 100, null)]), ct);

        return true;

        async Task<Guid> Ingredient(
            string name, MeasureUnit unit, decimal price, decimal size, decimal purchased, decimal? minStock = null) =>
            await Create(name, IngredientKind.Ingredient, unit, price, size, purchased, minStock);

        async Task<Guid> Packaging(string name, decimal price, decimal size, decimal purchased, decimal? minStock = null) =>
            await Create(name, IngredientKind.Packaging, MeasureUnit.Piece, price, size, purchased, minStock);

        async Task<Guid> Create(
            string name, IngredientKind kind, MeasureUnit unit, decimal price, decimal size, decimal purchased, decimal? minStock)
        {
            var id = await dispatcher.Send(new CreateIngredientCommand(name, kind, unit, price, size, minStock, null), ct);
            await dispatcher.Send(
                new RegisterPurchaseCommand(id, purchaseDay, purchased, price / size * purchased, true, null), ct);
            return id;
        }

        async Task<Guid> Product(string name, decimal yield, decimal price, params (Guid Id, decimal Quantity)[] lines) =>
            await dispatcher.Send(new CreateProductCommand(
                name, ProductType.Finished, yield, MeasureUnit.Piece, 0, price, null,
                lines.Select(l => Line(l.Id, l.Quantity)).ToList()), ct);

        async Task<Guid> Batch(DateOnly date, Guid productId, decimal quantity, decimal price, decimal sold) =>
            await dispatcher.Send(new CreateBatchCommand(date, productId, quantity, price, sold), ct);

        static RecipeLineInput Line(Guid ingredientId, decimal quantity) => new(ingredientId, null, quantity);
    }
}
