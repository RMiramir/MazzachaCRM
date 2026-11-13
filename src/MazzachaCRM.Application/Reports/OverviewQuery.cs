using MazzachaCRM.Application.Abstractions;
using MazzachaCRM.Application.Common;
using MazzachaCRM.Application.Orders;
using MazzachaCRM.Domain.Orders;
using MazzachaCRM.Domain.Settings;
using Microsoft.EntityFrameworkCore;

namespace MazzachaCRM.Application.Reports;

/// <summary>Итоги периода и того же отрезка предыдущего периода для сравнения.</summary>
public sealed record OverviewPeriodDto(
    string Title,
    DateOnly From,
    DateOnly To,
    decimal Revenue,
    decimal Profit,
    decimal Received,
    decimal PreviousRevenue,
    decimal PreviousProfit,
    decimal? RevenueChangePercent,
    decimal? ProfitChangePercent);

public sealed record OverviewGoalDto(
    decimal Goal,
    decimal Achieved,
    decimal Percent,
    decimal Remaining,
    int DaysLeft,
    decimal? PerDayNeeded);

public sealed record OverviewDto(
    DateOnly Today,
    IReadOnlyList<OverviewPeriodDto> Periods,
    IReadOnlyList<PeriodDayDto> Chart,
    OverviewGoalDto? Goal,
    IReadOnlyList<OrderDto> UpcomingOrders,
    decimal DebtsTotal,
    int DebtorsCount,
    int LowStockCount);

public sealed record GetOverviewQuery(DateOnly Today) : IQuery<OverviewDto>;

/// <summary>
/// Главная сводка: сегодня, неделя и месяц со сравнением, график за 30 дней, цель на месяц,
/// ближайшие заказы и долги. Итоги периодов считает тот же отчёт, что и «Сводка за период».
/// </summary>
public sealed class GetOverviewHandler(IReadDbContext db, CostingContextLoader costing)
    : IQueryHandler<GetOverviewQuery, OverviewDto>
{
    public async Task<OverviewDto> Handle(GetOverviewQuery query, CancellationToken ct)
    {
        var today = query.Today;
        var weekStart = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
        var monthStart = new DateOnly(today.Year, today.Month, 1);
        var previousMonthStart = monthStart.AddMonths(-1);
        var previousMonthEnd = previousMonthStart.AddDays(Math.Min(today.Day, DateTime.DaysInMonth(previousMonthStart.Year, previousMonthStart.Month)) - 1);

        var periods = new List<OverviewPeriodDto>
        {
            await Period("Сегодня", today, today, today.AddDays(-7), today.AddDays(-7), ct),
            await Period("Эта неделя", weekStart, today, weekStart.AddDays(-7), today.AddDays(-7), ct),
            await Period("Этот месяц", monthStart, today, previousMonthStart, previousMonthEnd, ct),
        };

        var chart = await Report(today.AddDays(-29), today, ct);
        var chartDays = Enumerable.Range(0, 30)
            .Select(offset => today.AddDays(offset - 29))
            .Select(date => chart.Days.FirstOrDefault(d => d.Date == date) ?? new PeriodDayDto(date, 0, 0, 0, 0, 0, 0, 0))
            .ToList();

        var settings = await db.Settings.OrderBy(s => s.Id).FirstOrDefaultAsync(ct) ?? AppSettings.CreateDefault();
        OverviewGoalDto? goal = null;
        if (settings.MonthlyProfitGoal is { } target and > 0)
        {
            var achieved = periods[2].Profit;
            var remaining = Math.Max(target - achieved, 0);
            var daysLeft = DateTime.DaysInMonth(today.Year, today.Month) - today.Day + 1;
            goal = new OverviewGoalDto(
                target,
                achieved,
                Math.Round(Math.Clamp(achieved / target * 100, 0, 999), 1),
                remaining.Money(),
                daysLeft,
                remaining > 0 ? (remaining / daysLeft).Money() : null);
        }

        var upcoming = await db.Orders
            .Include(o => o.Items)
            .Include(o => o.Payments)
            .Where(o => o.DueDate >= today && o.DueDate <= today.AddDays(7) &&
                        o.Status != OrderStatus.Delivered && o.Status != OrderStatus.Cancelled)
            .ToListAsync(ct);
        var mapper = await OrderMapper.CreateAsync(db, costing, upcoming, ct);

        var unpaid = (await db.Orders
                .Include(o => o.Items)
                .Include(o => o.Payments)
                .Where(o => o.Status == OrderStatus.Ready || o.Status == OrderStatus.Delivered)
                .ToListAsync(ct))
            .Where(o => o.Balance > 0)
            .ToList();

        var lowStock = (await db.Ingredients.ToListAsync(ct)).Count(i => i.IsLowStock || i.IsOutOfStock);

        return new OverviewDto(
            today,
            periods,
            chartDays,
            goal,
            upcoming.OrderBy(o => o.DueDate).ThenBy(o => o.DueTime ?? TimeOnly.MaxValue).Select(mapper.Map).ToList(),
            unpaid.Sum(o => o.Balance).Money(),
            unpaid.Select(o => o.CustomerId).Distinct().Count(),
            lowStock);
    }

    private async Task<OverviewPeriodDto> Period(
        string title, DateOnly from, DateOnly to, DateOnly previousFrom, DateOnly previousTo, CancellationToken ct)
    {
        var current = (await Report(from, to, ct)).Totals;
        var previous = (await Report(previousFrom, previousTo, ct)).Totals;
        return new OverviewPeriodDto(
            title,
            from,
            to,
            current.Revenue,
            current.Profit,
            current.Received,
            previous.Revenue,
            previous.Profit,
            Change(current.Revenue, previous.Revenue),
            Change(current.Profit, previous.Profit));
    }

    private Task<PeriodReportDto> Report(DateOnly from, DateOnly to, CancellationToken ct) =>
        new GetPeriodReportHandler(db).Handle(new GetPeriodReportQuery(from, to), ct);

    /// <summary>Изменение в процентах. Если в прошлом периоде был ноль или убыток, процент не имеет смысла.</summary>
    private static decimal? Change(decimal current, decimal previous) =>
        previous <= 0 ? null : Math.Round((current - previous) / previous * 100, 1, MidpointRounding.AwayFromZero);
}
