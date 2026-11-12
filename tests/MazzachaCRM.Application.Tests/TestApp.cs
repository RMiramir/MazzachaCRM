using MazzachaCRM.Application;
using MazzachaCRM.Application.Abstractions;
using MazzachaCRM.Infrastructure;
using MazzachaCRM.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace MazzachaCRM.Application.Tests;

/// <summary>
/// Приложение целиком на SQLite в памяти: настоящие обработчики, репозитории и миграции.
/// Каждая команда и каждый запрос выполняются в своей области, как отдельный HTTP-запрос.
/// </summary>
public sealed class TestApp : IAsyncDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly ServiceProvider _provider;

    private TestApp()
    {
        _connection.Open();
        _provider = new ServiceCollection()
            .AddApplication()
            .AddInfrastructure(_connection)
            .BuildServiceProvider();
    }

    public static async Task<TestApp> CreateAsync()
    {
        var app = new TestApp();
        await app.InScope(sp => sp.GetRequiredService<DatabaseInitializer>().InitializeAsync());
        return app;
    }

    public Task<TResult> Send<TResult>(ICommand<TResult> command) =>
        InScope(sp => sp.GetRequiredService<IDispatcher>().Send(command));

    public Task<TResult> Query<TResult>(IQuery<TResult> query) =>
        InScope(sp => sp.GetRequiredService<IDispatcher>().Query(query));

    public Task<bool> SeedDemo(DateOnly lastDay) =>
        InScope(sp => sp.GetRequiredService<DemoDataSeeder>().SeedAsync(lastDay));

    public async ValueTask DisposeAsync()
    {
        await _provider.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private async Task InScope(Func<IServiceProvider, Task> action)
    {
        await using var scope = _provider.CreateAsyncScope();
        await action(scope.ServiceProvider);
    }

    private async Task<T> InScope<T>(Func<IServiceProvider, Task<T>> action)
    {
        await using var scope = _provider.CreateAsyncScope();
        return await action(scope.ServiceProvider);
    }
}
