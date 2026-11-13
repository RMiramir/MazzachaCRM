using MazzachaCRM.Application.Abstractions;
using MazzachaCRM.Application.Expenses;
using MazzachaCRM.Application.Production;
using MazzachaCRM.Application.Reports;

namespace MazzachaCRM.Api.Endpoints;

/// <summary>Таблица дня: партии, продажи, списания, общие расходы, а также отчёты.</summary>
public static class DayEndpoints
{
    public static void MapDayEndpoints(this IEndpointRouteBuilder api)
    {
        api.MapGet("/days/{date}", (DateOnly date, IDispatcher dispatcher, CancellationToken ct) =>
                dispatcher.Query(new GetDayQuery(date), ct))
            .WithTags("День");

        api.MapPost("/days/{date}/copy-from/{fromDate}", async (DateOnly date, DateOnly fromDate, IDispatcher dispatcher, CancellationToken ct) =>
                Results.Ok(new { copied = await dispatcher.Send(new CopyDayCommand(fromDate, date), ct) }))
            .WithTags("День");

        var batches = api.MapGroup("/batches").WithTags("День");

        batches.MapPost("/", async (CreateBatchCommand command, IDispatcher dispatcher, CancellationToken ct) =>
            Results.Ok(new { id = await dispatcher.Send(command, ct) }));

        batches.MapPut("/{id:guid}", async (Guid id, UpdateBatchCommand command, IDispatcher dispatcher, CancellationToken ct) =>
        {
            await dispatcher.Send(command with { Id = id }, ct);
            return Results.NoContent();
        });

        batches.MapDelete("/{id:guid}", async (Guid id, IDispatcher dispatcher, CancellationToken ct) =>
        {
            await dispatcher.Send(new DeleteBatchCommand(id), ct);
            return Results.NoContent();
        });

        batches.MapPost("/{id:guid}/recalculate-cost", async (Guid id, IDispatcher dispatcher, CancellationToken ct) =>
        {
            await dispatcher.Send(new RecalculateBatchCostCommand(id), ct);
            return Results.NoContent();
        });

        batches.MapPut("/{id:guid}/sales/{date}", async (Guid id, DateOnly date, SalesRequest request, IDispatcher dispatcher, CancellationToken ct) =>
        {
            await dispatcher.Send(new SetBatchSalesCommand(id, date, request.Entries ?? []), ct);
            return Results.NoContent();
        });

        batches.MapPut("/{id:guid}/write-offs/{date}", async (Guid id, DateOnly date, WriteOffRequest request, IDispatcher dispatcher, CancellationToken ct) =>
        {
            await dispatcher.Send(new SetBatchWriteOffCommand(id, date, request.Quantity, request.Reason), ct);
            return Results.NoContent();
        });

        var expenses = api.MapGroup("/expenses").WithTags("День");

        expenses.MapPost("/", async (AddExpenseCommand command, IDispatcher dispatcher, CancellationToken ct) =>
            Results.Ok(new { id = await dispatcher.Send(command, ct) }));

        expenses.MapPut("/{id:guid}", async (Guid id, UpdateExpenseCommand command, IDispatcher dispatcher, CancellationToken ct) =>
        {
            await dispatcher.Send(command with { Id = id }, ct);
            return Results.NoContent();
        });

        expenses.MapDelete("/{id:guid}", async (Guid id, IDispatcher dispatcher, CancellationToken ct) =>
        {
            await dispatcher.Send(new DeleteExpenseCommand(id), ct);
            return Results.NoContent();
        });
    }

    public static void MapReportEndpoints(this IEndpointRouteBuilder api)
    {
        var group = api.MapGroup("/reports").WithTags("Отчёты");

        group.MapGet("/period", (DateOnly from, DateOnly to, IDispatcher dispatcher, CancellationToken ct) =>
            dispatcher.Query(new GetPeriodReportQuery(from, to), ct));

        group.MapGet("/margins", (IDispatcher dispatcher, CancellationToken ct) =>
            dispatcher.Query(new GetMarginReportQuery(), ct));

        group.MapGet("/overview", (DateOnly? today, IDispatcher dispatcher, CancellationToken ct) =>
            dispatcher.Query(new GetOverviewQuery(today ?? DateOnly.FromDateTime(DateTime.Now)), ct));
    }

    public sealed record SalesRequest(IReadOnlyList<SaleEntryInput>? Entries);

    public sealed record WriteOffRequest(decimal Quantity, string? Reason);
}
