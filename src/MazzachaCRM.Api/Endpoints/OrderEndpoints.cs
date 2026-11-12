using MazzachaCRM.Application.Abstractions;
using MazzachaCRM.Application.Customers;
using MazzachaCRM.Application.Orders;
using MazzachaCRM.Domain.Orders;

namespace MazzachaCRM.Api.Endpoints;

/// <summary>Клиенты и заказы.</summary>
public static class OrderEndpoints
{
    public static void MapCustomerEndpoints(this IEndpointRouteBuilder api)
    {
        var group = api.MapGroup("/customers").WithTags("Клиенты");

        group.MapGet("/", (string? search, IDispatcher dispatcher, CancellationToken ct) =>
            dispatcher.Query(new GetCustomersQuery(search), ct));

        group.MapPost("/", async (CreateCustomerCommand command, IDispatcher dispatcher, CancellationToken ct) =>
            Results.Ok(new { id = await dispatcher.Send(command, ct) }));

        group.MapPut("/{id:guid}", async (Guid id, UpdateCustomerCommand command, IDispatcher dispatcher, CancellationToken ct) =>
        {
            await dispatcher.Send(command with { Id = id }, ct);
            return Results.NoContent();
        });

        group.MapDelete("/{id:guid}", async (Guid id, IDispatcher dispatcher, CancellationToken ct) =>
        {
            await dispatcher.Send(new DeleteCustomerCommand(id), ct);
            return Results.NoContent();
        });
    }

    public static void MapOrderEndpoints(this IEndpointRouteBuilder api)
    {
        var group = api.MapGroup("/orders").WithTags("Заказы");

        group.MapGet("/", (bool? activeOnly, DateOnly? from, DateOnly? to, Guid? customerId, IDispatcher dispatcher, CancellationToken ct) =>
            dispatcher.Query(new GetOrdersQuery(activeOnly ?? false, from, to, customerId), ct));

        group.MapGet("/{id:guid}", (Guid id, IDispatcher dispatcher, CancellationToken ct) =>
            dispatcher.Query(new GetOrderQuery(id), ct));

        group.MapPost("/", async (CreateOrderCommand command, IDispatcher dispatcher, CancellationToken ct) =>
            Results.Ok(new { id = await dispatcher.Send(command, ct) }));

        group.MapPut("/{id:guid}", async (Guid id, UpdateOrderCommand command, IDispatcher dispatcher, CancellationToken ct) =>
        {
            await dispatcher.Send(command with { Id = id }, ct);
            return Results.NoContent();
        });

        group.MapPost("/{id:guid}/status", async (Guid id, StatusRequest request, IDispatcher dispatcher, CancellationToken ct) =>
        {
            await dispatcher.Send(new ChangeOrderStatusCommand(id, request.Status, request.Date), ct);
            return Results.NoContent();
        });

        group.MapDelete("/{id:guid}", async (Guid id, IDispatcher dispatcher, CancellationToken ct) =>
        {
            await dispatcher.Send(new DeleteOrderCommand(id), ct);
            return Results.NoContent();
        });
    }

    public sealed record StatusRequest(OrderStatus Status, DateOnly Date);
}
