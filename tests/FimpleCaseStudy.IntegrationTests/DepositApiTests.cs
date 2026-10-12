using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FimpleCaseStudy.Domain.Cash;
using FimpleCaseStudy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FimpleCaseStudy.IntegrationTests;

public sealed class DepositApiTests : IAsyncLifetime
{
    private static readonly JsonSerializerOptions ReceiptJson = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };
    private readonly ApiFixture fixture = new();
    public Task InitializeAsync() => fixture.InitializeAsync();
    public Task DisposeAsync() => fixture.DisposeAsync();
    private HttpClient Client => fixture.Client;

    private async Task<Guid> ReadyAsync(int first = 99, int second = 99, int deposit = 99)
    {
        var response = await Client.PostAsJsonAsync("/api/atms", new { name = "Deposit ATM" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var id = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await Client.PutAsJsonAsync($"/api/atms/{id}/configuration", new { cassettes = new[]
        {
            new { slot = 1, currency = "TRY", denomination = 100 }, new { slot = 2, currency = "TRY", denomination = 100 },
            new { slot = 3, currency = "TRY", denomination = 20 }, new { slot = 4, currency = "USD", denomination = 100 }
        }})).StatusCode);
        var lines = new List<object>
        {
            new { slot = 1, currency = "TRY", denomination = 100, count = first },
            new { slot = 2, currency = "TRY", denomination = 100, count = second }
        };
        if (deposit > 0) lines.Add(new { slot = 5, currency = "EUR", denomination = 50, count = deposit });
        Assert.Equal(HttpStatusCode.OK, (await Client.PostAsJsonAsync($"/api/atms/{id}/load", new { lines })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Client.PostAsJsonAsync($"/api/atms/{id}/state", new { state = "IN_SERVICE" })).StatusCode);
        return id;
    }
    private Task<HttpResponseMessage> DepositAsync(Guid atm, params DepositedNote[] notes) =>
        Client.PostAsJsonAsync($"/api/atms/{atm}/deposits", new { notes });

    private async Task<(int OnHand, int Reserved)> AssertLedgerAsync(Guid atmId)
    {
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CashManagementDbContext>();
        var atm = await db.Atms.Include(a => a.Cassettes).ThenInclude(c => c.Balances).SingleAsync(a => a.Id == atmId);
        var movements = await db.Movements.Where(m => m.AtmId == atmId).ToArrayAsync();
        foreach (var c in atm.Cassettes)
        {
            Assert.InRange(c.OnHand, 0, 100);
            Assert.Equal(c.OnHand, movements.Where(m => m.CassetteId == c.Id).Sum(m => m.PhysicalDelta));
            Assert.Equal(c.Reserved, movements.Where(m => m.CassetteId == c.Id).Sum(m => m.ReservedDelta));
        }
        return (atm.Cassettes.Sum(c => c.OnHand), atm.Cassettes.Sum(c => c.Reserved));
    }

    [Fact]
    public async Task Mixed_batch_returns_breakdown_persists_deposit_ledger_and_isolates_other_atm()
    {
        var atm = await ReadyAsync();
        var other = await ReadyAsync();
        var response = await DepositAsync(atm, new DepositedNote("TRY", 100, 5), new DepositedNote("EUR", 500, 2), new DepositedNote(null, null, 2, false));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var receipt = (await response.Content.ReadFromJsonAsync<DepositReceipt>(ReceiptJson))!;
        Assert.Equal(3, receipt.AcceptedCount); Assert.Equal(6, receipt.ReturnedCount);
        Assert.Equal(new[] { 1, 2, 5 }, receipt.Placements.Select(p => p.Slot));
        Assert.Equal(new[] { "CAPACITY_EXCEEDED", "CAPACITY_EXCEEDED", "UNRECOGNIZED" }, receipt.Returned.Select(r => r.Reason));
        Assert.Equal((300, 0), await AssertLedgerAsync(atm));
        Assert.Equal((297, 0), await AssertLedgerAsync(other));
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var movements = await scope.ServiceProvider.GetRequiredService<CashManagementDbContext>().Movements
            .Where(m => m.OperationId == receipt.OperationId).ToArrayAsync();
        Assert.Equal(3, movements.Length);
        Assert.All(movements, m => { Assert.Equal(MovementType.Deposit, m.Type); Assert.Equal(0, m.ReservedDelta); });
    }

    [Fact]
    public async Task Parallel_deposits_share_last_capacity_and_return_excess()
    {
        var atm = await ReadyAsync(100, 100, 99);
        var responses = await Task.WhenAll(DepositAsync(atm, new DepositedNote("TRY", 100, 2)), DepositAsync(atm, new DepositedNote("TRY", 100, 2)));
        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        var receipts = await Task.WhenAll(responses.Select(r => r.Content.ReadFromJsonAsync<DepositReceipt>(ReceiptJson)));
        Assert.Equal(1, receipts.Sum(r => r!.AcceptedCount));
        Assert.Equal(3, receipts.Sum(r => r!.ReturnedCount));
        Assert.Equal((300, 0), await AssertLedgerAsync(atm));
    }

    [Fact]
    public async Task Deposit_and_confirm_race_preserves_physical_and_reserved_ledger()
    {
        var atm = await ReadyAsync(100, 100, 0);
        var quoteResponse = await Client.PostAsJsonAsync($"/api/atms/{atm}/withdrawals/quotes",
            new { cardNumber = "9900000000000060", currency = "TRY", amount = 100 });
        Assert.Equal(HttpStatusCode.Created, quoteResponse.StatusCode);
        var quote = (await quoteResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var confirm = new HttpRequestMessage(HttpMethod.Post, $"/api/atms/{atm}/withdrawals/quotes/{quote}/confirm")
        { Content = JsonContent.Create(new { option = "exact" }) };
        confirm.Headers.Add("Idempotency-Key", "deposit-confirm-race");
        var responses = await Task.WhenAll(DepositAsync(atm, new DepositedNote("TRY", 100, 1)), Client.SendAsync(confirm));
        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        Assert.Equal(1, (await responses[0].Content.ReadFromJsonAsync<DepositReceipt>(ReceiptJson))!.AcceptedCount);
        Assert.Equal((200, 0), await AssertLedgerAsync(atm));
    }

    [Fact]
    public async Task Deposit_ledger_failure_rolls_back_every_accepted_line()
    {
        var atm = await ReadyAsync();
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CashManagementDbContext>();
        await db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION cash.reject_deposit() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN IF NEW."Type" = 'Deposit' THEN RAISE EXCEPTION 'forced deposit failure'; END IF; RETURN NEW; END $$;
            CREATE TRIGGER reject_deposit BEFORE INSERT ON cash.movements FOR EACH ROW EXECUTE FUNCTION cash.reject_deposit();
            """);
        var response = await DepositAsync(atm, new DepositedNote("TRY", 100, 3));
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.DoesNotContain("forced deposit failure", await response.Content.ReadAsStringAsync());
        Assert.Equal((297, 0), await AssertLedgerAsync(atm));
        Assert.Empty(await db.Movements.Where(m => m.Type == MovementType.Deposit).ToArrayAsync());
    }

    [Fact]
    public async Task Invalid_batch_is_problem_details_and_unknown_only_batch_has_no_cash_effect()
    {
        var atm = await ReadyAsync();
        var invalid = await DepositAsync(atm, new DepositedNote("TRY", 100, 1), new DepositedNote("EUR", 50, 0));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var problem = await invalid.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("INVALID_DEPOSIT_NOTES", problem.GetProperty("code").GetString());
        Assert.False(string.IsNullOrEmpty(problem.GetProperty("traceId").GetString()));
        var unknown = await DepositAsync(atm, new DepositedNote("GBP", 100, 2), new DepositedNote(null, null, 1, false));
        Assert.Equal(HttpStatusCode.OK, unknown.StatusCode);
        var receipt = (await unknown.Content.ReadFromJsonAsync<DepositReceipt>(ReceiptJson))!;
        Assert.Equal(0, receipt.AcceptedCount); Assert.Equal(3, receipt.ReturnedCount);
        Assert.Equal((297, 0), await AssertLedgerAsync(atm));
    }

    [Fact]
    public async Task Missing_atm_and_maintenance_return_expected_errors()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await DepositAsync(Guid.NewGuid(), new DepositedNote("TRY", 20, 1))).StatusCode);
        var atm = await ReadyAsync();
        Assert.Equal(HttpStatusCode.OK, (await Client.PostAsJsonAsync($"/api/atms/{atm}/state", new { state = "MAINTENANCE" })).StatusCode);
        var response = await DepositAsync(atm, new DepositedNote("TRY", 20, 1));
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("INVALID_ATM_STATE", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        Assert.Equal((297, 0), await AssertLedgerAsync(atm));
    }
}
