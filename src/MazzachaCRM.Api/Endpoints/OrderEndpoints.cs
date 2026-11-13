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

        group.MapGet("/", (OrderScope? scope, DateOnly? from, DateOnly? to, Guid? customerId, IDispatcher dispatcher, CancellationToken ct) =>
            dispatcher.Query(new GetOrdersQuery(scope ?? OrderScope.All, from, to, customerId), ct));

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

        group.MapPost("/{id:guid}/payments", async (Guid id, PaymentRequest request, IDispatcher dispatcher, CancellationToken ct) =>
            Results.Ok(new { id = await dispatcher.Send(new AddOrderPaymentCommand(id, request.Date, request.Amount, request.Note), ct) }));

        group.MapDelete("/{id:guid}/payments/{paymentId:guid}", async (Guid id, Guid paymentId, IDispatcher dispatcher, CancellationToken ct) =>
        {
            await dispatcher.Send(new RemoveOrderPaymentCommand(id, paymentId), ct);
            return Results.NoContent();
        });

        group.MapPost("/{id:guid}/shop-report", async (Guid id, ShopReportRequest request, IDispatcher dispatcher, CancellationToken ct) =>
        {
            await dispatcher.Send(new SubmitShopReportCommand(id, request.Date, request.Items ?? []), ct);
            return Results.NoContent();
        });

        group.MapPost("/{id:guid}/repeat", async (Guid id, RepeatRequest request, IDispatcher dispatcher, CancellationToken ct) =>
            Results.Ok(new { id = await dispatcher.Send(new RepeatOrderCommand(id, request.DueDate), ct) }));
    }

    public sealed record PaymentRequest(DateOnly Date, decimal Amount, string? Note);

    public sealed record ShopReportRequest(DateOnly Date, IReadOnlyList<ShopReportItemInput>? Items);

    public sealed record RepeatRequest(DateOnly DueDate);

    public sealed record StatusRequest(OrderStatus Status, DateOnly Date);
}
