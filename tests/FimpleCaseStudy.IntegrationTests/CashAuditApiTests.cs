using System.Data;
using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FimpleCaseStudy.Application.Atms;
using FimpleCaseStudy.Domain.Cash;
using FimpleCaseStudy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace FimpleCaseStudy.IntegrationTests;

public sealed class CashAuditApiTests : IAsyncLifetime
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    { Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseUpper, false) } };
    private readonly SnapshotBarrier barrier = new();
    private readonly ApiFixture fixture;
    public CashAuditApiTests() => fixture = new(configureServices: services =>
        services.AddDbContext<CashManagementDbContext>(options => options.AddInterceptors(barrier)));
    public Task InitializeAsync() => fixture.InitializeAsync();
    public Task DisposeAsync() => fixture.DisposeAsync();
    private HttpClient Client => fixture.Client;

    private async Task<Guid> ReadyAsync()
    {
        var create = await Client.PostAsJsonAsync("/api/atms", new { name = "Audit ATM" });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var id = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await Client.PutAsJsonAsync($"/api/atms/{id}/configuration", new
        {
            cassettes = new CassetteConfiguration[] { new(1, Currency.TRY, 100), new(2, Currency.TRY, 50),
                new(3, Currency.TRY, 20), new(4, Currency.USD, 100) }
        }, Json)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Client.PostAsJsonAsync($"/api/atms/{id}/load", new CashRequest(
            [new(1, Currency.TRY, 100, 10), new(3, Currency.TRY, 20, 10), new(4, Currency.USD, 100, 2), new(5, Currency.EUR, 50, 2)]), Json)).StatusCode);
        await StateAsync(id, "IN_SERVICE");
        return id;
    }
    private async Task StateAsync(Guid id, string state) => Assert.Equal(HttpStatusCode.OK,
        (await Client.PostAsJsonAsync($"/api/atms/{id}/state", new { state })).StatusCode);
    private static ReconciliationRequest Counts(int first = 9) => new(
    [
        new(1, [new(Currency.TRY, 100, first)]), new(2, [new(Currency.TRY, 50, 1)]),
        new(3, [new(Currency.TRY, 20, 10)]), new(4, [new(Currency.USD, 100, 2)]),
        new(5, [new(Currency.USD, 20, 3)])
    ]);
    private Task<HttpResponseMessage> ReconcileAsync(Guid id, ReconciliationRequest? counts = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/atms/{id}/reconciliations")
        { Content = JsonContent.Create(counts ?? Counts(), options: Json) };
        request.Headers.Add("X-User", "alice");
        return Client.SendAsync(request);
    }
    private async Task<ConsistencyReport> ConsistencyAsync(Guid id) =>
        (await Client.GetFromJsonAsync<ConsistencyReport>($"/api/atms/{id}/consistency", Json))!;

    [Fact]
    public async Task All_seven_movement_types_replay_counts_and_reconciliation_survives_restart()
    {
        var id = await ReadyAsync();
        var other = await ReadyAsync();
        var quoteResponse = await Client.PostAsJsonAsync($"/api/atms/{id}/withdrawals/quotes",
            new { cardNumber = "9900000000000070", currency = "TRY", amount = 100 });
        Assert.Equal(HttpStatusCode.Created, quoteResponse.StatusCode);
        var quote = (await quoteResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        Assert.Contains((await ConsistencyAsync(id)).Lines, l => l.StoredReserved == 1 && l.LedgerReserved == 1);
        var confirm = new HttpRequestMessage(HttpMethod.Post, $"/api/atms/{id}/withdrawals/quotes/{quote}/confirm")
        { Content = JsonContent.Create(new { option = "exact" }) };
        confirm.Headers.Add("Idempotency-Key", "audit-confirm");
        Assert.Equal(HttpStatusCode.OK, (await Client.SendAsync(confirm)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Client.PostAsJsonAsync($"/api/atms/{id}/deposits",
            new { notes = new[] { new DepositedNote("TRY", 100, 2), new("EUR", 20, 1) } })).StatusCode);
        await StateAsync(id, "MAINTENANCE");
        Assert.Equal(HttpStatusCode.OK, (await Client.PostAsJsonAsync($"/api/atms/{id}/unload",
            new CashRequest([new(3, Currency.TRY, 20, 1)]), Json)).StatusCode);
        var response = await ReconcileAsync(id);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var receipt = (await response.Content.ReadFromJsonAsync<ReconciliationReceipt>(Json))!;
        Assert.Equal("alice", receipt.Actor);
        Assert.Contains(receipt.Lines, l => l.Slot == 1 && l.StoredCount == 11 && l.CountedCount == 9 && l.Delta == -2);
        Assert.True((await ConsistencyAsync(id)).IsConsistent);
        Assert.All((await ConsistencyAsync(other)).Lines, l => Assert.Equal(l.StoredOnHand, l.LedgerOnHand));
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CashManagementDbContext>();
            Assert.Equal(Enum.GetValues<MovementType>().Order(), (await db.Movements.Where(m => m.AtmId == id)
                .Select(m => m.Type).Distinct().ToArrayAsync()).Order());
            Assert.Single(await db.Reconciliations.Where(r => r.AtmId == id).ToArrayAsync());
            Assert.Empty(await db.Reconciliations.Where(r => r.AtmId == other).ToArrayAsync());
        }
        await fixture.RestartApiAsync();
        var saved = (await Client.GetFromJsonAsync<ReconciliationReceipt>(response.Headers.Location, Json))!;
        Assert.Equal(receipt.OperationId, saved.OperationId); Assert.Equal(receipt.Lines, saved.Lines);
        Assert.Equal(HttpStatusCode.NotFound, (await Client.GetAsync($"/api/atms/{other}/reconciliations/{receipt.OperationId}")).StatusCode);
        var approval = new HttpRequestMessage(HttpMethod.Post, $"/api/atms/{id}/reconciliations/{receipt.OperationId}/approve");
        approval.Headers.Add("X-User", "bob");
        Assert.Equal(HttpStatusCode.OK, (await Client.SendAsync(approval)).StatusCode);
        var repeated = await ReconcileAsync(id);
        Assert.Equal(HttpStatusCode.Created, repeated.StatusCode);
        Assert.All((await repeated.Content.ReadFromJsonAsync<ReconciliationReceipt>(Json))!.Lines, l => Assert.Equal(0, l.Delta));
    }

    [Fact]
    public async Task Report_groups_mixed_notes_and_separates_reserved_available_and_deposit()
    {
        var id = await ReadyAsync();
        Assert.Equal(HttpStatusCode.Created, (await Client.PostAsJsonAsync($"/api/atms/{id}/withdrawals/quotes",
            new { cardNumber = "9900000000000071", currency = "TRY", amount = 100 })).StatusCode);
        var report = (await Client.GetFromJsonAsync<AtmCashReport>($"/api/atms/{id}/report", Json))!;
        var tr = Assert.Single(report.Totals, t => t.Currency == Currency.TRY);
        Assert.Equal((20, 1, 19, 1200m, 100m, 1100m),
            (tr.OnHandNotes, tr.ReservedNotes, tr.AvailableNotes, tr.OnHandAmount, tr.ReservedAmount, tr.AvailableAmount));
        Assert.Equal(200m, Assert.Single(report.Totals, t => t.Currency == Currency.USD).OnHandAmount);
        Assert.Equal(100m, Assert.Single(report.Totals, t => t.Currency == Currency.EUR).OnHandAmount);
        var deposit = Assert.Single(report.Cassettes, c => c.Cassette.Slot == 5);
        Assert.Equal(CassetteKind.Deposit, deposit.Cassette.Kind);
        Assert.Equal(2, deposit.Cassette.Available); // Accounting availability; kind forbids dispensing.
        Assert.Equal(100m, Assert.Single(deposit.Totals, t => t.Currency == Currency.EUR).AvailableAmount);
        var note = Assert.Single(report.Notes, n => n.Currency == Currency.TRY && n.Denomination == 100);
        Assert.Equal((10, 1, 9), (note.OnHand, note.Reserved, note.Available));
    }

    [Fact]
    public async Task Consistency_detects_physical_reserved_and_missing_balance_drift_without_repair()
    {
        var id = await ReadyAsync();
        Assert.True((await ConsistencyAsync(id)).IsConsistent);
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CashManagementDbContext>();
        var atm = await db.Atms.Include(a => a.Cassettes).SingleAsync(a => a.Id == id);
        var first = atm.Cassettes.Single(c => c.Slot == 1).Id;
        var deposit = atm.Cassettes.Single(c => c.Slot == 5).Id;
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cash.balances SET \"OnHand\" = 9, \"Reserved\" = 1 WHERE \"CassetteId\" = {first}");
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM cash.balances WHERE \"CassetteId\" = {deposit}");
        var report = await ConsistencyAsync(id);
        Assert.False(report.IsConsistent);
        var line = Assert.Single(report.Lines, l => l.Slot == 1);
        Assert.Equal((9, 1, 10L, 0L), (line.StoredOnHand, line.StoredReserved, line.LedgerOnHand, line.LedgerReserved));
        Assert.Contains(report.Lines, l => l.Slot == 5 && l.StoredOnHand == 0 && l.LedgerOnHand == 2);
        Assert.False((await ConsistencyAsync(id)).IsConsistent);
    }

    [Fact]
    public async Task Adjustment_insert_failure_rolls_back_count_history_and_balances()
    {
        var id = await ReadyAsync(); await StateAsync(id, "MAINTENANCE");
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CashManagementDbContext>();
        await db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION cash.reject_adjustment() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN IF NEW."Type" = 'ReconciliationAdjustment' THEN RAISE EXCEPTION 'forced count failure'; END IF; RETURN NEW; END $$;
            CREATE TRIGGER reject_adjustment BEFORE INSERT ON cash.movements FOR EACH ROW EXECUTE FUNCTION cash.reject_adjustment();
            """);
        var response = await ReconcileAsync(id);
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.DoesNotContain("forced count failure", await response.Content.ReadAsStringAsync());
        Assert.Empty(await db.Reconciliations.ToArrayAsync());
        Assert.Empty(await db.Movements.Where(m => m.Type == MovementType.ReconciliationAdjustment).ToArrayAsync());
        var consistency = await ConsistencyAsync(id);
        Assert.True(consistency.IsConsistent);
        Assert.Equal(10, Assert.Single(consistency.Lines, l => l.Slot == 1).StoredOnHand);
    }

    [Fact]
    public async Task Invalid_inventory_and_wrong_state_return_problems_and_maintenance_releases_quote_before_count()
    {
        var id = await ReadyAsync();
        Assert.Equal(HttpStatusCode.Conflict, (await ReconcileAsync(id)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await Client.PostAsJsonAsync($"/api/atms/{id}/withdrawals/quotes",
            new { cardNumber = "9900000000000072", currency = "TRY", amount = 100 })).StatusCode);
        await StateAsync(id, "MAINTENANCE");
        Assert.All((await ConsistencyAsync(id)).Lines, l => Assert.Equal(0, l.StoredReserved));
        var response = await ReconcileAsync(id, new(Counts().Cassettes!.Take(4).ToArray()));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("INVALID_CASSETTE_COUNTS", problem.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("traceId").GetString()));
        Assert.True((await ConsistencyAsync(id)).IsConsistent);
        Assert.Equal(HttpStatusCode.Created, (await ReconcileAsync(id)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await ReconcileAsync(Guid.NewGuid())).StatusCode);
        foreach (var path in new[] { "report", "consistency", $"reconciliations/{Guid.NewGuid()}" })
            Assert.Equal(HttpStatusCode.NotFound, (await Client.GetAsync($"/api/atms/{Guid.NewGuid()}/{path}")).StatusCode);
    }

    [Fact]
    public async Task Parallel_reconciliation_and_manual_load_serialize_to_valid_ledger()
    {
        var id = await ReadyAsync(); await StateAsync(id, "MAINTENANCE");
        var tasks = await Task.WhenAll(ReconcileAsync(id, Counts(100)), Client.PostAsJsonAsync($"/api/atms/{id}/load",
            new CashRequest([new(1, Currency.TRY, 100, 1)]), Json));
        Assert.Equal(HttpStatusCode.Created, tasks[0].StatusCode);
        Assert.Contains(tasks[1].StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.Conflict });
        var receipt = (await tasks[0].Content.ReadFromJsonAsync<ReconciliationReceipt>(Json))!;
        Assert.Equal(tasks[1].StatusCode == HttpStatusCode.OK ? 11 : 10,
            Assert.Single(receipt.Lines, l => l.Slot == 1).StoredCount);
        var report = await ConsistencyAsync(id);
        Assert.True(report.IsConsistent); Assert.Equal(100, Assert.Single(report.Lines, l => l.Slot == 1).StoredOnHand);
    }

    [Fact]
    public async Task Consistency_uses_one_snapshot_when_deposit_commits_between_its_queries()
    {
        var id = await ReadyAsync();
        barrier.Arm();
        var pending = ConsistencyAsync(id);
        await barrier.Started.Task.WaitAsync(TimeSpan.FromSeconds(15));
        try
        {
            Assert.Equal(HttpStatusCode.OK, (await Client.PostAsJsonAsync($"/api/atms/{id}/deposits",
                new { notes = new[] { new DepositedNote("TRY", 100, 1) } })).StatusCode);
        }
        finally { barrier.Resume.TrySetResult(); }
        var oldSnapshot = await pending;
        Assert.True(oldSnapshot.IsConsistent);
        Assert.Equal(10, Assert.Single(oldSnapshot.Lines, l => l.Slot == 1).LedgerOnHand);
        var current = await ConsistencyAsync(id);
        Assert.True(current.IsConsistent); Assert.Equal(11, Assert.Single(current.Lines, l => l.Slot == 1).LedgerOnHand);
    }

    private sealed class SnapshotBarrier : DbCommandInterceptor
    {
        private int armed;
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Resume { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Arm() => Interlocked.Exchange(ref armed, 1);
        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (command.Transaction?.IsolationLevel == IsolationLevel.RepeatableRead &&
                command.CommandText.Contains("cash.atms", StringComparison.Ordinal) && Interlocked.Exchange(ref armed, 0) == 1)
            {
                Started.TrySetResult();
                await Resume.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
            }
            return result;
        }
    }
}
