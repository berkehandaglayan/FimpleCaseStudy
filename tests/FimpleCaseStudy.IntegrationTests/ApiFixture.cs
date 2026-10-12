using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Testcontainers.PostgreSql;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace FimpleCaseStudy.IntegrationTests;

public sealed class ApiFixture(TimeProvider? clock = null, Action<IServiceCollection>? configureServices = null) : IAsyncLifetime
{
    public CapturedLogs Logs { get; } = new();
    public PostgreSqlContainer Database { get; } = new PostgreSqlBuilder("postgres:17")
        .Build();

    public WebApplicationFactory<Program> Factory { get; private set; } = null!;
    public HttpClient Client { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await Database.StartAsync();
        StartApi();
    }

    private void StartApi()
    {
        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.UseSetting("ConnectionStrings:Database", Database.GetConnectionString())
                .UseSetting("Withdrawals:HmacKey", "test-only-hmac-key-not-for-production-050")
                .ConfigureServices(services =>
                {
                    services.AddLogging(logging => logging.AddProvider(Logs));
                    if (clock is not null)
                    {
                        services.RemoveAll<TimeProvider>();
                        services.AddSingleton(clock);
                    }
                    configureServices?.Invoke(services);
                }));
        Client = Factory.CreateClient();
    }

    public async Task RestartApiAsync()
    {
        Client.Dispose();
        await Factory.DisposeAsync();
        StartApi();
    }

    public async Task DisposeAsync()
    {
        Client?.Dispose();
        if (Factory is not null) await Factory.DisposeAsync();
        await Database.DisposeAsync();
    }
}

public sealed class CapturedLogs : ILoggerProvider
{
    public ConcurrentQueue<string> Messages { get; } = new();
    public ILogger CreateLogger(string categoryName) => new CapturingLogger(Messages);
    public void Dispose() { }
    private sealed class CapturingLogger(ConcurrentQueue<string> messages) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => messages.Enqueue(formatter(state, exception));
    }
}
