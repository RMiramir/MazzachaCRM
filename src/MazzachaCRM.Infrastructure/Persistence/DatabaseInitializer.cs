using MazzachaCRM.Domain.Settings;
using Microsoft.EntityFrameworkCore;

namespace MazzachaCRM.Infrastructure.Persistence;

/// <summary>При запуске применяет миграции и создаёт запись настроек, если её ещё нет.</summary>
public sealed class DatabaseInitializer(AppDbContext db)
{
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await db.Database.MigrateAsync(ct);

        if (!await db.Settings.AnyAsync(ct))
        {
            db.Settings.Add(AppSettings.CreateDefault());
            await db.SaveChangesAsync(ct);
        }
    }
}
