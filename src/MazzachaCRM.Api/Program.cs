using System.Diagnostics;
using System.Text.Json.Serialization;
using MazzachaCRM.Api;
using MazzachaCRM.Api.Endpoints;
using MazzachaCRM.Application;
using MazzachaCRM.Infrastructure;
using MazzachaCRM.Infrastructure.Persistence;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Routing;

var builder = WebApplication.CreateBuilder(args);

// Файл базы лежит в папке пользователя, чтобы пересборка проекта его не затрагивала.
var databasePath = builder.Configuration["Database:Path"] is { Length: > 0 } configured
    ? configured
    : Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MazzachaCRM",
        "mazzacha.db");

builder.Services.AddApplication();
builder.Services.AddInfrastructure(databasePath);

builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = true);
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddOpenApi();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    await scope.ServiceProvider.GetRequiredService<DatabaseInitializer>().InitializeAsync();

    if (args.Contains("--seed-demo"))
    {
        var seeded = await scope.ServiceProvider
            .GetRequiredService<DemoDataSeeder>()
            .SeedAsync(DateOnly.FromDateTime(DateTime.Now));
        app.Logger.LogInformation(seeded
            ? "Демо-данные загружены."
            : "Демо-данные пропущены: в базе уже есть записи.");
    }
}

app.Logger.LogInformation("База данных: {Path}", Path.GetFullPath(databasePath));

app.UseExceptionHandler();
// Страница интерфейса не кэшируется: после обновления программы браузер сразу берёт новую версию.
// Файлы скриптов и стилей можно кэшировать — у каждой сборки у них новые имена.
var noCacheForPages = new StaticFileOptions
{
    OnPrepareResponse = context =>
    {
        if (context.File.Name.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
            context.Context.Response.Headers.CacheControl = "no-cache, no-store, must-revalidate";
    },
};

app.UseDefaultFiles();
app.UseStaticFiles(noCacheForPages);

app.MapOpenApi();

var api = app.MapGroup("/api");
api.MapSettingsEndpoints();
api.MapIngredientEndpoints();
api.MapStockEndpoints();
api.MapProductEndpoints();
api.MapDayEndpoints();
api.MapCustomerEndpoints();
api.MapOrderEndpoints();
api.MapReportEndpoints();
api.MapFallback(() => Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Не найдено"));

// Всё остальное отдаёт фронтенд (маршрутизация на стороне браузера).
app.MapFallbackToFile("index.html", noCacheForPages);

if (args.Contains("--open-browser"))
{
    app.Lifetime.ApplicationStarted.Register(() =>
    {
        var url = app.Urls.FirstOrDefault() ?? "http://localhost:5080";
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            app.Logger.LogWarning(exception, "Не удалось открыть браузер. Откройте {Url} вручную.", url);
        }
    });
}

try
{
    app.Run();
}
catch (IOException exception) when (exception.InnerException is AddressInUseException || exception.Message.Contains("address already in use", StringComparison.OrdinalIgnoreCase))
{
    // Чаще всего это старое окно программы: браузер тогда показывает старую версию.
    Console.Error.WriteLine();
    Console.Error.WriteLine("Адрес уже занят: программа, скорее всего, уже запущена в другом окне.");
    Console.Error.WriteLine("Закройте старое окно программы и запустите её снова.");
    Environment.ExitCode = 1;
}

public partial class Program;
