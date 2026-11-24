using FluentValidation;
using MazzachaCRM.Application.Abstractions;
using MazzachaCRM.Domain.Settings;
using Microsoft.EntityFrameworkCore;

namespace MazzachaCRM.Application.Settings;

public sealed record SettingsDto(decimal HourlyRate, string Currency, decimal? MonthlyProfitGoal, string? BusinessName, string DatabasePath);

public sealed record GetSettingsQuery : IQuery<SettingsDto>;

public sealed class GetSettingsHandler(IReadDbContext db, IDatabaseInfo database)
    : IQueryHandler<GetSettingsQuery, SettingsDto>
{
    public async Task<SettingsDto> Handle(GetSettingsQuery query, CancellationToken ct)
    {
        var settings = await db.Settings.OrderBy(s => s.Id).FirstOrDefaultAsync(ct) ?? AppSettings.CreateDefault();
        return new SettingsDto(settings.HourlyRate, settings.Currency, settings.MonthlyProfitGoal, settings.BusinessName, database.DatabasePath);
    }
}

public sealed record UpdateSettingsCommand(decimal HourlyRate, string Currency, decimal? MonthlyProfitGoal = null, string? BusinessName = null) : ICommand<Unit>;

public sealed class UpdateSettingsValidator : AbstractValidator<UpdateSettingsCommand>
{
    public UpdateSettingsValidator()
    {
        RuleFor(x => x.HourlyRate).GreaterThanOrEqualTo(0).WithName("Ставка за час");
        RuleFor(x => x.Currency).NotEmpty().MaximumLength(20).WithName("Валюта");
        RuleFor(x => x.BusinessName).MaximumLength(100).WithName("Название кондитерской");
        RuleFor(x => x.MonthlyProfitGoal).GreaterThanOrEqualTo(0).WithName("Цель на месяц");
    }
}

public sealed class UpdateSettingsHandler(ISettingsRepository settings)
    : ICommandHandler<UpdateSettingsCommand, Unit>
{
    public async Task<Unit> Handle(UpdateSettingsCommand command, CancellationToken ct)
    {
        var current = await settings.GetAsync(ct);
        current.Update(command.HourlyRate, command.Currency, command.MonthlyProfitGoal);
        current.Rename(command.BusinessName);
        return Unit.Value;
    }
}
