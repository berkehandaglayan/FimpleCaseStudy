using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FimpleCaseStudy.Domain.Cash;
using FimpleCaseStudy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace FimpleCaseStudy.IntegrationTests;

public sealed class AtmApiTests : IAsyncLifetime
{
    private readonly ApiFixture fixture = new();
    private HttpClient Client => fixture.Client;
    public Task InitializeAsync() => fixture.InitializeAsync();
    public Task DisposeAsync() => fixture.DisposeAsync();

    private async Task<Guid> CreateAsync(string name = "Test ATM")
    {
        var response = await Client.PostAsJsonAsync("/api/atms", new { name });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("UNCONFIGURED", body.GetProperty("state").GetString());
        Assert.Equal(5, body.GetProperty("cassettes").GetArrayLength());
        return body.GetProperty("id").GetGuid();
    }

    private async Task<Guid> ConfigureAsync()
    {
        var id = await CreateAsync();
        var response = await Client.PutAsJsonAsync($"/api/atms/{id}/configuration", new
        {
            cassettes = new[]
            {
                new { slot = 1, currency = "TRY", denomination = 100 },
                new { slot = 2, currency = "TRY", denomination = 50 },
                new { slot = 3, currency = "TRY", denomination = 20 },
                new { slot = 4, currency = "USD", denomination = 100 }
            }
        });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return id;
    }

    private async Task StateAsync(Guid id, string state)
    {
        var response = await Client.PostAsJsonAsync($"/api/atms/{id}/state", new { state });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
    }

    private Task<HttpResponseMessage> LoadAsync(Guid id, int count) => Client.PostAsJsonAsync($"/api/atms/{id}/load", new
    {
        lines = new[] { new { slot = 1, currency = "TRY", denomination = 100, count } }
    });

    [Fact]
    public async Task Lifecycle_persists_cash_and_ledger_without_affecting_another_atm()
    {
        var id = await ConfigureAsync();
        var otherId = await CreateAsync("Other ATM");
        Client.DefaultRequestHeaders.Add("X-User", "operator-a");
        var loaded = await Client.PostAsJsonAsync($"/api/atms/{id}/load", new
        {
            lines = new[]
            {
                new { slot = 1, currency = "TRY", denomination = 100, count = 10 },
                new { slot = 5, currency = "EUR", denomination = 50, count = 2 }
            }
        });
        Assert.True(loaded.IsSuccessStatusCode, await loaded.Content.ReadAsStringAsync());
        await StateAsync(id, "IN_SERVICE");
        Assert.Equal(HttpStatusCode.Conflict, (await LoadAsync(id, 1)).StatusCode);
        await StateAsync(id, "MAINTENANCE");
        var unloaded = await Client.PostAsJsonAsync($"/api/atms/{id}/unload", new
        {
            lines = new[] { new { slot = 1, currency = "TRY", denomination = 100, count = 3 } }
        });
        Assert.True(unloaded.IsSuccessStatusCode, await unloaded.Content.ReadAsStringAsync());
        await StateAsync(id, "IN_SERVICE");

        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CashManagementDbContext>();
        var atm = await db.Atms.Include(a => a.Cassettes).ThenInclude(c => c.Balances).SingleAsync(a => a.Id == id);
        Assert.Equal(AtmState.InService, atm.State);
        Assert.Equal(7, atm.Cassettes.Single(c => c.Slot == 1).OnHand);
        Assert.Equal(2, atm.Cassettes.Single(c => c.Slot == 5).OnHand);
        var movements = await db.Movements.Where(m => m.AtmId == id).ToArrayAsync();
        Assert.Equal(3, movements.Length);
        Assert.All(movements, m => Assert.Equal("operator-a", m.Actor));
        foreach (var cassette in atm.Cassettes)
        {
            Assert.Equal(cassette.OnHand, movements.Where(m => m.CassetteId == cassette.Id).Sum(m => m.PhysicalDelta));
        }
        Assert.Equal(AtmState.Unconfigured, (await db.Atms.SingleAsync(a => a.Id == otherId)).State);
        Assert.False(await db.Movements.AnyAsync(m => m.AtmId == otherId));
        var listed = await Client.GetFromJsonAsync<JsonElement>("/api/atms?limit=10&offset=0");
        Assert.Equal(2, listed.GetArrayLength());
    }

    [Fact]
    public async Task Invalid_batch_preserves_cash_state_and_ledger()
    {
        var id = await ConfigureAsync();
        var response = await Client.PostAsJsonAsync($"/api/atms/{id}/load", new
        {
            lines = new[]
            {
                new { slot = 1, currency = "TRY", denomination = 100, count = 10 },
                new { slot = 5, currency = "TRY", denomination = 20, count = 60 },
                new { slot = 5, currency = "EUR", denomination = 50, count = 41 }
            }
        });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("CASSETTE_CAPACITY_EXCEEDED", body.GetProperty("code").GetString());
        Assert.True(body.TryGetProperty("traceId", out _));
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CashManagementDbContext>();
        var atm = await db.Atms.Include(a => a.Cassettes).ThenInclude(c => c.Balances).SingleAsync(a => a.Id == id);
        Assert.Equal(AtmState.Configured, atm.State);
        Assert.All(atm.Cassettes, c => Assert.Equal(0, c.OnHand));
        Assert.Empty(await db.Movements.ToArrayAsync());
        var missing = await Client.GetAsync($"/api/atms/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task Parallel_loads_cannot_exceed_capacity_or_lose_movements()
    {
        var id = await ConfigureAsync();
        Assert.Equal(HttpStatusCode.OK, (await LoadAsync(id, 50)).StatusCode);
        await StateAsync(id, "IN_SERVICE");
        await StateAsync(id, "MAINTENANCE");
        var results = await Task.WhenAll(LoadAsync(id, 30), LoadAsync(id, 30));
        Assert.Single(results, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Single(results, r => r.StatusCode == HttpStatusCode.Conflict);
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CashManagementDbContext>();
        var atm = await db.Atms.Include(a => a.Cassettes).ThenInclude(c => c.Balances).SingleAsync(a => a.Id == id);
        Assert.Equal(80, atm.Cassettes.Single(c => c.Slot == 1).OnHand);
        Assert.Equal(2, await db.Movements.CountAsync());
        Assert.Equal(80, await db.Movements.SumAsync(m => m.PhysicalDelta));
    }

    [Fact]
    public async Task Ledger_is_append_only_in_ef_and_direct_sql()
    {
        var id = await ConfigureAsync();
        Assert.Equal(HttpStatusCode.OK, (await LoadAsync(id, 10)).StatusCode);
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CashManagementDbContext>();
        db.Movements.Remove(await db.Movements.SingleAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
        foreach (var sql in new[] { "UPDATE cash.movements SET \"Actor\" = 'changed'", "DELETE FROM cash.movements", "TRUNCATE cash.movements" })
        {
            var error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(sql));
            Assert.Equal("55000", error.SqlState);
        }
        Assert.Equal(1, await db.Movements.CountAsync());
    }

    [Fact]
    public async Task Ledger_write_failure_rolls_back_balances_and_lifecycle()
    {
        var id = await ConfigureAsync();
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CashManagementDbContext>();
        await db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION cash.fail_test_insert() RETURNS trigger AS $$
            BEGIN RAISE EXCEPTION 'forced ledger failure'; END; $$ LANGUAGE plpgsql;
            CREATE TRIGGER fail_test_insert BEFORE INSERT ON cash.movements
            FOR EACH STATEMENT EXECUTE FUNCTION cash.fail_test_insert();
            """);
        var response = await LoadAsync(id, 10);
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.DoesNotContain("forced ledger failure", await response.Content.ReadAsStringAsync());
        var atm = await db.Atms.Include(a => a.Cassettes).ThenInclude(c => c.Balances).SingleAsync(a => a.Id == id);
        Assert.Equal(AtmState.Configured, atm.State);
        Assert.All(atm.Cassettes, c => Assert.Equal(0, c.OnHand));
        Assert.Equal(0, await db.Movements.CountAsync());
    }

    [Theory]
    [InlineData("{\"state\":99}")]
    [InlineData("{\"state\":\"UNKNOWN\"}")]
    [InlineData("{")]
    public async Task Malformed_or_unknown_state_is_a_bad_request(string json)
    {
        var id = await CreateAsync();
        var response = await Client.PostAsync($"/api/atms/{id}/state", new StringContent(json, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }
}
