using MazzachaCRM.Domain.Common;

namespace MazzachaCRM.Domain.Settings;

/// <summary>Настройки приложения. В базе всегда одна запись.</summary>
public sealed class AppSettings : AggregateRoot
{
    public static readonly Guid SingletonId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private AppSettings()
    {
    }

    /// <summary>Стоимость часа работы. Ноль означает, что работа в себестоимость не входит.</summary>
    public decimal HourlyRate { get; private set; }

    public string Currency { get; private set; } = "сомони";

    public static AppSettings CreateDefault() => new() { Id = SingletonId };

    public void Update(decimal hourlyRate, string currency)
    {
        HourlyRate = Guard.NotNegative(hourlyRate, "Ставка за час");
        Currency = Guard.NotEmpty(currency, "Валюта");
    }
}

public interface ISettingsRepository
{
    /// <summary>Возвращает настройки, создавая запись при первом обращении.</summary>
    Task<AppSettings> GetAsync(CancellationToken ct);
}
