using MazzachaCRM.Application.Abstractions;
using MazzachaCRM.Application.Ingredients;
using MazzachaCRM.Application.Products;
using MazzachaCRM.Application.Settings;
using MazzachaCRM.Application.Stock;

namespace MazzachaCRM.Api.Endpoints;

/// <summary>Справочники: настройки, продукты, склад, изделия.</summary>
public static class CatalogEndpoints
{
    public static void MapSettingsEndpoints(this IEndpointRouteBuilder api)
    {
        var group = api.MapGroup("/settings").WithTags("Настройки");

        group.MapGet("/", (IDispatcher dispatcher, CancellationToken ct) =>
            dispatcher.Query(new GetSettingsQuery(), ct));

        group.MapPut("/", async (UpdateSettingsCommand command, IDispatcher dispatcher, CancellationToken ct) =>
        {
            await dispatcher.Send(command, ct);
            return Results.NoContent();
        });
    }

    public static void MapIngredientEndpoints(this IEndpointRouteBuilder api)
    {
        var group = api.MapGroup("/ingredients").WithTags("Продукты");

        group.MapGet("/", (IDispatcher dispatcher, CancellationToken ct) =>
            dispatcher.Query(new GetIngredientsQuery(), ct));

        group.MapPost("/", async (CreateIngredientCommand command, IDispatcher dispatcher, CancellationToken ct) =>
            Results.Ok(new { id = await dispatcher.Send(command, ct) }));

        group.MapPut("/{id:guid}", async (Guid id, UpdateIngredientCommand command, IDispatcher dispatcher, CancellationToken ct) =>
        {
            await dispatcher.Send(command with { Id = id }, ct);
            return Results.NoContent();
        });

        group.MapDelete("/{id:guid}", async (Guid id, IDispatcher dispatcher, CancellationToken ct) =>
        {
            await dispatcher.Send(new DeleteIngredientCommand(id), ct);
            return Results.NoContent();
        });

        group.MapPost("/{id:guid}/purchases", async (Guid id, RegisterPurchaseCommand command, IDispatcher dispatcher, CancellationToken ct) =>
            Results.Ok(new { id = await dispatcher.Send(command with { IngredientId = id }, ct) }));

        group.MapPost("/{id:guid}/adjustments", async (Guid id, AdjustStockCommand command, IDispatcher dispatcher, CancellationToken ct) =>
            Results.Ok(new { id = await dispatcher.Send(command with { IngredientId = id }, ct) }));

        group.MapPost("/{id:guid}/write-offs", async (Guid id, WriteOffStockCommand command, IDispatcher dispatcher, CancellationToken ct) =>
            Results.Ok(new { id = await dispatcher.Send(command with { IngredientId = id }, ct) }));
    }

    public static void MapStockEndpoints(this IEndpointRouteBuilder api)
    {
        var group = api.MapGroup("/stock").WithTags("Склад");

        group.MapGet("/movements", (Guid? ingredientId, DateOnly? from, DateOnly? to, IDispatcher dispatcher, CancellationToken ct) =>
            dispatcher.Query(new GetStockMovementsQuery(ingredientId, from, to), ct));

        group.MapDelete("/movements/{id:guid}", async (Guid id, IDispatcher dispatcher, CancellationToken ct) =>
        {
            await dispatcher.Send(new DeleteStockMovementCommand(id), ct);
            return Results.NoContent();
        });
    }

    public static void MapProductEndpoints(this IEndpointRouteBuilder api)
    {
        var group = api.MapGroup("/products").WithTags("Изделия");

        group.MapGet("/", (bool? includeArchived, IDispatcher dispatcher, CancellationToken ct) =>
            dispatcher.Query(new GetProductsQuery(includeArchived ?? false), ct));

        group.MapGet("/{id:guid}", (Guid id, IDispatcher dispatcher, CancellationToken ct) =>
            dispatcher.Query(new GetProductQuery(id), ct));

        group.MapPost("/", async (CreateProductCommand command, IDispatcher dispatcher, CancellationToken ct) =>
            Results.Ok(new { id = await dispatcher.Send(command, ct) }));

        group.MapPut("/{id:guid}", async (Guid id, UpdateProductCommand command, IDispatcher dispatcher, CancellationToken ct) =>
        {
            await dispatcher.Send(command with { Id = id }, ct);
            return Results.NoContent();
        });

        group.MapDelete("/{id:guid}", async (Guid id, IDispatcher dispatcher, CancellationToken ct) =>
        {
            await dispatcher.Send(new DeleteProductCommand(id), ct);
            return Results.NoContent();
        });

        group.MapPost("/{id:guid}/archive", async (Guid id, ArchiveRequest request, IDispatcher dispatcher, CancellationToken ct) =>
        {
            await dispatcher.Send(new SetProductArchivedCommand(id, request.Archived), ct);
            return Results.NoContent();
        });

        group.MapPost("/cost-preview", (PreviewProductCostQuery query, IDispatcher dispatcher, CancellationToken ct) =>
            dispatcher.Query(query, ct));
    }

    public sealed record ArchiveRequest(bool Archived);
}
