using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FimpleCaseStudy.Api;
using FimpleCaseStudy.Application.Atms;
using FimpleCaseStudy.Application.Withdrawals;
using FimpleCaseStudy.Domain.Cash;
using FimpleCaseStudy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace FimpleCaseStudy.IntegrationTests;

public sealed class ReportingAndOutboxTests : IAsyncLifetime
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    { Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseUpper, false) } };
    private readonly AdjustableClock clock = new(new DateTimeOffset(2026, 10, 12, 13, 0, 0, TimeSpan.Zero));
    private readonly RecordingSink sink = new();
    private readonly ReportBarrier barrier = new();
    private readonly ApiFixture fixture;
    private bool enableWorker;
    public ReportingAndOutboxTests() => fixture = new(clock, services =>
    {
        services.AddDbContext<CashManagementDbContext>(options => options.AddInterceptors(barrier));
        if (!enableWorker)
        {
            services.Remove(services.Single(d => d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(LowCashAlertWorker)));
            services.RemoveAll<ILowCashAlertSink>(); services.AddSingleton<ILowCashAlertSink>(sink);
        }
    });
    public Task InitializeAsync() => fixture.InitializeAsync();
    public Task DisposeAsync() => fixture.DisposeAsync();
    private HttpClient Client => fixture.Client;

    private async Task<Guid> ReadyAsync(int first = 10, bool service = true, bool deposit = false)
    {
        var response = await Client.PostAsJsonAsync("/api/atms", new { name = "Outbox ATM" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var id = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await Client.PutAsJsonAsync($"/api/atms/{id}/configuration", new ConfigureAtmRequest(
            [new(1, Currency.TRY, 100), new(2, Currency.TRY, 50), new(3, Currency.TRY, 20), new(4, Currency.USD, 100)]), Json)).StatusCode);
        var lines = new List<CashLine> { new(1, Currency.TRY, 100, first), new(2, Currency.TRY, 50, 10),
            new(3, Currency.TRY, 20, 10), new(4, Currency.USD, 100, 10) };
        if (deposit) lines.Add(new(5, Currency.EUR, 500, 2));
        Assert.Equal(HttpStatusCode.OK, (await Client.PostAsJsonAsync($"/api/atms/{id}/load", new CashRequest(lines), Json)).StatusCode);
        if (service) await StateAsync(id, "IN_SERVICE");
        return id;
    }
    private async Task StateAsync(Guid id, string state) => Assert.Equal(HttpStatusCode.OK,
        (await Client.PostAsJsonAsync($"/api/atms/{id}/state", new { state })).StatusCode);
    private Task<HttpResponseMessage> QuoteAsync(Guid id) => Client.PostAsJsonAsync($"/api/atms/{id}/withdrawals/quotes",
        new { cardNumber = "9900000000000090", currency = "TRY", amount = 100 });
    private async Task<Guid> QuoteIdAsync(Guid id)
    {
        var response = await QuoteAsync(id); Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }
    private Task<HttpResponseMessage> ConfirmAsync(Guid id, Guid quote)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/atms/{id}/withdrawals/quotes/{quote}/confirm")
        { Content = JsonContent.Create(new { option = "exact" }) };
        request.Headers.Add("Idempotency-Key", "outbox-" + quote);
        return Client.SendAsync(request);
    }
    private static ReconciliationRequest Counts(int first) => new(
    [
        new(1, [new(Currency.TRY, 100, first)]), new(2, [new(Currency.TRY, 50, 10)]),
        new(3, [new(Currency.TRY, 20, 10)]), new(4, [new(Currency.USD, 100, 10)]), new(5, [])
    ]);
    private Task<LowCashAlertView[]?> AlertsAsync(Guid id) => Client.GetFromJsonAsync<LowCashAlertView[]>($"/api/atms/{id}/alerts", Json);
    private async Task<int> DispatchAsync(int size = 20, CancellationToken ct = default)
    {
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IOutboxDispatcher>().ProcessBatchAsync(size, ct);
    }

    [Fact]
    public async Task Report_converts_all_cash_and_reservations_with_rates_from_one_snapshot()
    {
        var id = await ReadyAsync(deposit: true); await QuoteIdAsync(id);
        var report = (await Client.GetFromJsonAsync<AtmCashReport>($"/api/atms/{id}/report", Json))!;
        Assert.Equal(new TryTotals(86700m, 100m, 86600m), report.OverallTry);
        Assert.Equal(40000m, report.Cassettes.Single(c => c.Cassette.Slot == 4).TotalTry.OnHandAmount);
        Assert.Equal(45000m, report.Cassettes.Single(c => c.Cassette.Slot == 5).TotalTry.OnHandAmount);
        Assert.False(report.Cassettes.Single(c => c.Cassette.Slot == 5).LowCash);
        Assert.True(report.Cassettes.Single(c => c.Cassette.Slot == 1).LowCash);
        Assert.Equal(report.OverallTry.OnHandAmount, report.Cassettes.Sum(c => c.TotalTry.OnHandAmount));
        barrier.Arm();
        var pending = Client.GetFromJsonAsync<AtmCashReport>($"/api/atms/{id}/report", Json);
        await barrier.Started.Task.WaitAsync(TimeSpan.FromSeconds(15));
        try
        {
            await using var scope = fixture.Factory.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<CashManagementDbContext>().Database
                .ExecuteSqlRawAsync("UPDATE cash.exchange_rates SET \"RateToTry\" = 90 WHERE \"Currency\" = 'EUR';");
        }
        finally { barrier.Resume.TrySetResult(); }
        Assert.Equal(86700m, (await pending)!.OverallTry.OnHandAmount);
        Assert.Equal(131700m, (await Client.GetFromJsonAsync<AtmCashReport>($"/api/atms/{id}/report", Json))!.OverallTry.OnHandAmount);
    }

    [Fact]
    public async Task Quote_cancel_confirm_replay_deposit_expiry_unload_and_count_use_net_threshold_changes()
    {
        var id = await ReadyAsync(); Assert.Empty((await AlertsAsync(id))!);
        var q1 = await QuoteIdAsync(id);
        var alert = Assert.Single((await AlertsAsync(id))!);
        Assert.Equal((10, 9, q1), (alert.BeforeAvailable, alert.Available, alert.OperationId));
        Assert.Equal(HttpStatusCode.OK, (await Client.PostAsync($"/api/atms/{id}/withdrawals/quotes/{q1}/cancel", null)).StatusCode);
        var q2 = await QuoteIdAsync(id);
        var confirm = await ConfirmAsync(id, q2); Assert.Equal(HttpStatusCode.OK, confirm.StatusCode);
        Assert.Equal(await confirm.Content.ReadAsStringAsync(), await (await ConfirmAsync(id, q2)).Content.ReadAsStringAsync());
        Assert.Equal(2, (await AlertsAsync(id))!.Length); // Release+withdrawal and replay do not re-alert.
        Assert.Equal(HttpStatusCode.OK, (await Client.PostAsJsonAsync($"/api/atms/{id}/deposits",
            new { notes = new[] { new DepositedNote("TRY", 100, 1) } })).StatusCode);
        await QuoteIdAsync(id); Assert.Equal(3, (await AlertsAsync(id))!.Length);
        clock.Advance(TimeSpan.FromSeconds(61));
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<IWithdrawalService>().ExpireAsync(default);
        await QuoteIdAsync(id); Assert.Equal(4, (await AlertsAsync(id))!.Length);
        await StateAsync(id, "MAINTENANCE");
        Assert.Equal(HttpStatusCode.OK, (await Client.PostAsJsonAsync($"/api/atms/{id}/unload", new CashRequest([new(1, Currency.TRY, 100, 1)]), Json)).StatusCode);
        Assert.Equal(5, (await AlertsAsync(id))!.Length);
        Assert.Equal(HttpStatusCode.OK, (await Client.PostAsJsonAsync($"/api/atms/{id}/load", new CashRequest([new(1, Currency.TRY, 100, 1)]), Json)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await Client.PostAsJsonAsync($"/api/atms/{id}/reconciliations", Counts(9), Json)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await Client.PostAsJsonAsync($"/api/atms/{id}/reconciliations", Counts(9), Json)).StatusCode);
        Assert.Equal(6, (await AlertsAsync(id))!.Length);
        Assert.True((await Client.GetFromJsonAsync<ConsistencyReport>($"/api/atms/{id}/consistency", Json))!.IsConsistent);
    }

    [Fact]
    public async Task First_service_alerts_low_stock_once_and_deposit_cassette_never_alerts()
    {
        var id = await ReadyAsync(9, service: false, deposit: true);
        Assert.Empty((await AlertsAsync(id))!);
        await StateAsync(id, "IN_SERVICE");
        var first = Assert.Single((await AlertsAsync(id))!);
        Assert.Equal("INITIAL_SERVICE_LOW_CASH", first.Reason); Assert.Equal(1, first.Slot);
        await StateAsync(id, "MAINTENANCE"); await StateAsync(id, "IN_SERVICE");
        await QuoteIdAsync(id); // 9->8 remains low, not a new crossing.
        Assert.Single((await AlertsAsync(id))!);
        await StateAsync(id, "MAINTENANCE");
        Assert.Equal(HttpStatusCode.OK, (await Client.PostAsJsonAsync($"/api/atms/{id}/unload", new CashRequest([new(5, Currency.EUR, 500, 1)]), Json)).StatusCode);
        Assert.All((await AlertsAsync(id))!, e => Assert.InRange(e.Slot, 1, 4));
        Assert.Equal(HttpStatusCode.BadRequest, (await Client.GetAsync($"/api/atms/{id}/alerts?limit=0")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Client.GetAsync($"/api/atms/{Guid.NewGuid()}/alerts")).StatusCode);
    }

    [Fact]
    public async Task Outbox_insert_failure_rolls_back_quote_manual_cash_and_reconciliation()
    {
        var id = await ReadyAsync();
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CashManagementDbContext>();
        var before = await db.Movements.CountAsync();
        await db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION cash.reject_outbox() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN RAISE EXCEPTION 'forced outbox failure'; END $$;
            CREATE TRIGGER reject_outbox BEFORE INSERT ON cash.outbox FOR EACH ROW EXECUTE FUNCTION cash.reject_outbox();
            """);
        var failure = await QuoteAsync(id); Assert.Equal(HttpStatusCode.InternalServerError, failure.StatusCode);
        Assert.DoesNotContain("forced outbox failure", await failure.Content.ReadAsStringAsync());
        Assert.Empty(await db.WithdrawalQuotes.ToArrayAsync());
        await StateAsync(id, "MAINTENANCE");
        Assert.Equal(HttpStatusCode.InternalServerError, (await Client.PostAsJsonAsync($"/api/atms/{id}/unload", new CashRequest([new(1, Currency.TRY, 100, 1)]), Json)).StatusCode);
        Assert.Equal(HttpStatusCode.InternalServerError, (await Client.PostAsJsonAsync($"/api/atms/{id}/reconciliations", Counts(9), Json)).StatusCode);
        Assert.Empty(await db.Reconciliations.ToArrayAsync()); Assert.Empty(await db.LowCashAlerts.ToArrayAsync());
        Assert.Equal(before, await db.Movements.CountAsync());
        var consistency = (await Client.GetFromJsonAsync<ConsistencyReport>($"/api/atms/{id}/consistency", Json))!;
        Assert.True(consistency.IsConsistent);
        Assert.Equal((10, 0), (consistency.Lines.Single(l => l.Slot == 1).StoredOnHand, consistency.Lines.Single(l => l.Slot == 1).StoredReserved));
    }

    [Fact]
    public async Task Delivery_failure_retries_after_five_seconds_and_restart_preserves_pending_event()
    {
        var id = await ReadyAsync(9); var eventId = Assert.Single((await AlertsAsync(id))!).Id;
        sink.FailNext = 1;
        Assert.Equal(1, await DispatchAsync());
        var failed = Assert.Single((await AlertsAsync(id))!); Assert.Null(failed.SentAt); Assert.Equal(1, failed.Attempts);
        Assert.Equal(0, await DispatchAsync());
        await fixture.RestartApiAsync(); clock.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(1, await DispatchAsync());
        var sent = Assert.Single((await AlertsAsync(id))!);
        Assert.Equal(eventId, sent.Id); Assert.Equal(2, sent.Attempts); Assert.NotNull(sent.SentAt);
        Assert.Equal(0, await DispatchAsync()); Assert.Single(sink.Published);
        Assert.DoesNotContain(fixture.Logs.Messages, m => m.Contains("publisher-sensitive-failure", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Parallel_dispatchers_skip_the_locked_event_instead_of_publishing_it_twice()
    {
        var id = await ReadyAsync(9); sink.PauseNext = 1;
        var first = DispatchAsync(1);
        await sink.Started.Task.WaitAsync(TimeSpan.FromSeconds(15));
        try { Assert.Equal(0, await DispatchAsync(1)); }
        finally { sink.Resume.TrySetResult(); }
        Assert.Equal(1, await first); Assert.Single(sink.Published);
        Assert.NotNull(Assert.Single((await AlertsAsync(id))!).SentAt);
    }

    [Fact]
    public async Task Crash_window_after_publish_before_sent_commit_can_repeat_the_same_event_id()
    {
        var id = await ReadyAsync(9); var eventId = Assert.Single((await AlertsAsync(id))!).Id;
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CashManagementDbContext>();
        await db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION cash.reject_sent() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN IF NEW."SentAt" IS NOT NULL THEN RAISE EXCEPTION 'forced sent failure'; END IF; RETURN NEW; END $$;
            CREATE TRIGGER reject_sent BEFORE UPDATE ON cash.outbox FOR EACH ROW EXECUTE FUNCTION cash.reject_sent();
            """);
        await Assert.ThrowsAsync<DbUpdateException>(() => DispatchAsync(1));
        var pending = Assert.Single((await AlertsAsync(id))!); Assert.Null(pending.SentAt); Assert.Equal(0, pending.Attempts);
        await db.Database.ExecuteSqlRawAsync("DROP TRIGGER reject_sent ON cash.outbox; DROP FUNCTION cash.reject_sent();");
        Assert.Equal(1, await DispatchAsync(1));
        Assert.Equal(new[] { eventId, eventId }, sink.Published.Select(e => e.Id));
    }

    [Fact]
    public async Task Cancellation_releases_claim_and_leaves_event_pending_for_next_worker()
    {
        var id = await ReadyAsync(9); sink.PauseNext = 1;
        using var cts = new CancellationTokenSource();
        var pending = DispatchAsync(1, cts.Token);
        await sink.Started.Task.WaitAsync(TimeSpan.FromSeconds(15)); cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        var alert = Assert.Single((await AlertsAsync(id))!); Assert.Null(alert.SentAt); Assert.Equal(0, alert.Attempts);
        Assert.Equal(1, await DispatchAsync(1)); Assert.Single(sink.Published);
    }

    [Fact]
    public async Task Hosted_worker_logs_and_marks_existing_pending_event_sent_after_restart()
    {
        var id = await ReadyAsync(9); var eventId = Assert.Single((await AlertsAsync(id))!).Id;
        enableWorker = true; await fixture.RestartApiAsync();
        var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
        LowCashAlertView? sent = null;
        while (DateTimeOffset.UtcNow < deadline)
        {
            sent = Assert.Single((await AlertsAsync(id))!);
            if (sent.SentAt is not null) break;
            await Task.Delay(100);
        }
        Assert.NotNull(sent!.SentAt); Assert.Equal(1, sent.Attempts);
        Assert.Contains(fixture.Logs.Messages, m => m.Contains("Low-cash alert " + eventId, StringComparison.Ordinal));
    }

    private sealed class RecordingSink : ILowCashAlertSink
    {
        public ConcurrentQueue<LowCashAlertView> Published { get; } = new();
        public int FailNext;
        public int PauseNext;
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Resume { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task PublishAsync(LowCashAlertView alert, CancellationToken ct)
        {
            if (Interlocked.Exchange(ref FailNext, 0) == 1) throw new InvalidOperationException("publisher-sensitive-failure");
            if (Interlocked.Exchange(ref PauseNext, 0) == 1)
            {
                Started.TrySetResult(); await Resume.Task.WaitAsync(ct);
            }
            Published.Enqueue(alert);
        }
    }
    private sealed class ReportBarrier : DbCommandInterceptor
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
                Started.TrySetResult(); await Resume.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
            }
            return result;
        }
    }
}
