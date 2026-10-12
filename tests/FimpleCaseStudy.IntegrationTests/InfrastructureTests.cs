using System.Net;
using System.Text.Json;
using FimpleCaseStudy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FimpleCaseStudy.IntegrationTests;

public sealed class InfrastructureTests : IAsyncLifetime
{
    private readonly ApiFixture fixture = new();

    public Task InitializeAsync() => fixture.InitializeAsync();
    public Task DisposeAsync() => fixture.DisposeAsync();

    [Fact]
    public async Task Startup_applies_migrations_to_real_database_and_is_ready()
    {
        var response = await fixture.Client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CashManagementDbContext>();
        Assert.NotEmpty(await db.Database.GetAppliedMigrationsAsync());
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.Equal("cash", await db.Database.SqlQueryRaw<string>(
            "SELECT schema_name AS \"Value\" FROM information_schema.schemata WHERE schema_name = 'cash'")
            .SingleAsync());
    }

    [Fact]
    public async Task Unknown_route_returns_problem_details_with_code_and_trace()
    {
        var response = await fixture.Client.GetAsync("/api/does-not-exist");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("RESOURCE_NOT_FOUND", body.RootElement.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(body.RootElement.GetProperty("traceId").GetString()));
    }

    [Fact]
    public async Task OpenApi_document_contains_system_endpoint()
    {
        var response = await fixture.Client.GetAsync("/swagger/v1/swagger.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(body.RootElement.GetProperty("paths").TryGetProperty("/api/info", out _));
    }

    [Fact]
    public async Task Database_outage_fails_readiness_but_not_liveness()
    {
        await fixture.Database.StopAsync();
        var live = await fixture.Client.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        var ready = await fixture.Client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.StatusCode);
        using var body = JsonDocument.Parse(await ready.Content.ReadAsStringAsync());
        Assert.Equal("DATABASE_NOT_READY", body.RootElement.GetProperty("code").GetString());
    }
}
