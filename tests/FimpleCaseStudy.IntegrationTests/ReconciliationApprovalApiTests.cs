using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FimpleCaseStudy.Application.Atms;
using FimpleCaseStudy.Domain.Cash;
using FimpleCaseStudy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace FimpleCaseStudy.IntegrationTests;

public sealed class ReconciliationApprovalApiTests : IAsyncLifetime
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    { Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseUpper, false) } };
    private readonly ApiFixture fixture = new();
    public Task InitializeAsync() => fixture.InitializeAsync();
    public Task DisposeAsync() => fixture.DisposeAsync();
    private HttpClient Client => fixture.Client;

    private async Task<Guid> ReadyAsync()
    {
        var response = await Client.PostAsJsonAsync("/api/atms", new { name = "Approval ATM" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var id = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await Client.PutAsJsonAsync($"/api/atms/{id}/configuration", new ConfigureAtmRequest(
            [new(1, Currency.TRY, 100), new(2, Currency.TRY, 50), new(3, Currency.TRY, 20), new(4, Currency.USD, 100)]), Json)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Client.PostAsJsonAsync($"/api/atms/{id}/load",
            new CashRequest([new(1, Currency.TRY, 100, 20)]), Json)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await StateAsync(id, "IN_SERVICE")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await StateAsync(id, "MAINTENANCE")).StatusCode);
        return id;
    }
    private Task<HttpResponseMessage> StateAsync(Guid id, string state) =>
        Client.PostAsJsonAsync($"/api/atms/{id}/state", new { state });
    private static ReconciliationRequest Counts(int first = 9, int usd = 0, IReadOnlyList<CountedNote>? deposit = null) => new(
    [
        new(1, [new(Currency.TRY, 100, first)]), new(2, [new(Currency.TRY, 50, 0)]),
        new(3, [new(Currency.TRY, 20, 0)]), new(4, [new(Currency.USD, 100, usd)]), new(5, deposit ?? [])
    ]);
    private Task<HttpResponseMessage> CountAsync(Guid id, ReconciliationRequest? counts = null, string? actor = " Alice ") =>
        SendAsync($"/api/atms/{id}/reconciliations", actor, JsonContent.Create(counts ?? Counts(), options: Json));
    private Task<HttpResponseMessage> ApproveAsync(Guid id, Guid operation, string? actor = "bob") =>
        SendAsync($"/api/atms/{id}/reconciliations/{operation}/approve", actor);
    private Task<HttpResponseMessage> SendAsync(string url, string? actor, HttpContent? content = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
        if (actor is not null) request.Headers.TryAddWithoutValidation("X-User", actor);
        return Client.SendAsync(request);
    }
    private static async Task<ReconciliationView> ViewAsync(HttpResponseMessage response, HttpStatusCode expected = HttpStatusCode.Created)
    {
        Assert.Equal(expected, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ReconciliationView>(Json))!;
    }
    private static async Task CodeAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(code, body.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("traceId").GetString()));
    }

    [Theory]
    [InlineData(10, 1000, ReconciliationStatus.Completed)]
    [InlineData(9, 1100, ReconciliationStatus.AwaitingApproval)]
    public async Task Exact_threshold_and_above_control_service_without_reapplying_adjustments(int counted, int difference, ReconciliationStatus expected)
    {
        var id = await ReadyAsync();
        var view = await ViewAsync(await CountAsync(id, Counts(counted)));
        Assert.Equal(expected, view.Status); Assert.Equal(difference, view.TotalDifferenceTry); Assert.Equal("alice", view.Actor);
        Assert.Equal(new[] { 1m, 40m, 45m }, view.ExchangeRates!.Select(r => r.RateToTry));
        if (expected == ReconciliationStatus.AwaitingApproval)
        {
            await CodeAsync(await StateAsync(id, "IN_SERVICE"), HttpStatusCode.Conflict, "RECONCILIATION_APPROVAL_PENDING");
            await CodeAsync(await CountAsync(id, Counts(counted)), HttpStatusCode.Conflict, "RECONCILIATION_APPROVAL_PENDING");
            await CodeAsync(await ApproveAsync(id, view.OperationId, "ALICE"), HttpStatusCode.Conflict, "SAME_USER_APPROVAL");
            var approved = await ViewAsync(await ApproveAsync(id, view.OperationId, " BoB "), HttpStatusCode.OK);
            Assert.Equal(ReconciliationStatus.Approved, approved.Status); Assert.Equal("bob", approved.ApprovedBy);
            Assert.NotNull(approved.ApprovedAt);
            var replay = await ViewAsync(await ApproveAsync(id, view.OperationId, "BOB"), HttpStatusCode.OK);
            Assert.Equal(approved, replay with { Lines = approved.Lines, ExchangeRates = approved.ExchangeRates });
            await CodeAsync(await ApproveAsync(id, view.OperationId, "carol"), HttpStatusCode.Conflict, "RECONCILIATION_ALREADY_APPROVED");
        }
        else await CodeAsync(await ApproveAsync(id, view.OperationId), HttpStatusCode.Conflict, "APPROVAL_NOT_REQUIRED");
        Assert.Equal(HttpStatusCode.OK, (await StateAsync(id, "IN_SERVICE")).StatusCode);
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CashManagementDbContext>();
        var adjustment = Assert.Single(await db.Movements.Where(m => m.Type == MovementType.ReconciliationAdjustment).ToArrayAsync());
        Assert.Equal(counted - 20, adjustment.PhysicalDelta);
        Assert.True((await Client.GetFromJsonAsync<ConsistencyReport>($"/api/atms/{id}/consistency", Json))!.IsConsistent);
    }

    [Fact]
    public async Task Mixed_fx_and_signed_differences_are_snapshotted_and_pending_survives_restart()
    {
        var id = await ReadyAsync(); var other = await ReadyAsync();
        var view = await ViewAsync(await CountAsync(id, Counts(10, 1, [new(Currency.EUR, 20, 1)])));
        Assert.Equal(5900m, view.TotalDifferenceTry); // |-1000| +4000+900, not signed net3900.
        Assert.Equal(ReconciliationStatus.AwaitingApproval, view.Status);
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CashManagementDbContext>();
            await db.Database.ExecuteSqlRawAsync("UPDATE cash.exchange_rates SET \"RateToTry\" = 80 WHERE \"Currency\" = 'USD';");
        }
        await fixture.RestartApiAsync();
        var saved = (await Client.GetFromJsonAsync<ReconciliationView>($"/api/atms/{id}/reconciliations/{view.OperationId}", Json))!;
        Assert.Equal(5900m, saved.TotalDifferenceTry);
        Assert.Equal(40m, Assert.Single(saved.ExchangeRates!, r => r.Currency == Currency.USD).RateToTry);
        Assert.Equal(80m, Assert.Single((await Client.GetFromJsonAsync<ExchangeRateSnapshot[]>("/api/exchange-rates", Json))!, r => r.Currency == Currency.USD).RateToTry);
        await CodeAsync(await StateAsync(id, "IN_SERVICE"), HttpStatusCode.Conflict, "RECONCILIATION_APPROVAL_PENDING");
        Assert.Equal(HttpStatusCode.OK, (await StateAsync(other, "IN_SERVICE")).StatusCode);
        await CodeAsync(await ApproveAsync(other, view.OperationId), HttpStatusCode.NotFound, "RECONCILIATION_NOT_FOUND");
        Assert.Equal(HttpStatusCode.OK, (await ApproveAsync(id, view.OperationId)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await StateAsync(id, "IN_SERVICE")).StatusCode);
    }

    [Fact]
    public async Task Approval_actor_is_required_and_canonical_and_multiple_headers_are_rejected()
    {
        var id = await ReadyAsync();
        foreach (var actor in new[] { " ", "alice,bob", "ali ce", new string('a', 129) })
            await CodeAsync(await CountAsync(id, actor: actor), HttpStatusCode.BadRequest, "INVALID_ACTOR");
        var view = await ViewAsync(await CountAsync(id, actor: null));
        Assert.Equal("system", view.Actor);
        await CodeAsync(await ApproveAsync(id, view.OperationId, null), HttpStatusCode.BadRequest, "ACTOR_REQUIRED");
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/atms/{id}/reconciliations/{view.OperationId}/approve");
        request.Headers.TryAddWithoutValidation("X-User", new[] { "bob", "carol" });
        await CodeAsync(await Client.SendAsync(request), HttpStatusCode.BadRequest, "INVALID_ACTOR");
        await CodeAsync(await ApproveAsync(id, view.OperationId, "SYSTEM"), HttpStatusCode.Conflict, "SAME_USER_APPROVAL");
        await CodeAsync(await ApproveAsync(id, Guid.NewGuid()), HttpStatusCode.NotFound, "RECONCILIATION_NOT_FOUND");
        await CodeAsync(await ApproveAsync(Guid.NewGuid(), view.OperationId), HttpStatusCode.NotFound, "ATM_NOT_FOUND");
        Assert.Equal(HttpStatusCode.OK, (await ApproveAsync(id, view.OperationId)).StatusCode);
    }

    [Fact]
    public async Task Parallel_approvers_and_service_transition_have_one_approval_and_no_duplicate_movement()
    {
        var id = await ReadyAsync(); var view = await ViewAsync(await CountAsync(id));
        var responses = await Task.WhenAll(ApproveAsync(id, view.OperationId, "bob"), ApproveAsync(id, view.OperationId, "carol"), StateAsync(id, "IN_SERVICE"));
        Assert.Single(responses[..2], r => r.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses[..2], r => r.StatusCode == HttpStatusCode.Conflict);
        Assert.Contains(responses[2].StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.Conflict });
        var saved = (await Client.GetFromJsonAsync<ReconciliationView>($"/api/atms/{id}/reconciliations/{view.OperationId}", Json))!;
        Assert.Equal(ReconciliationStatus.Approved, saved.Status);
        if (responses[2].StatusCode == HttpStatusCode.Conflict)
            Assert.Equal(HttpStatusCode.OK, (await StateAsync(id, "IN_SERVICE")).StatusCode);
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CashManagementDbContext>();
        Assert.Single(await db.Movements.Where(m => m.OperationId == view.OperationId).ToArrayAsync());
        Assert.Equal(HttpStatusCode.OK, (await ApproveAsync(id, view.OperationId, saved.ApprovedBy)).StatusCode);
    }

    [Fact]
    public async Task Parallel_approval_and_new_count_cannot_erase_an_unapproved_reconciliation()
    {
        var id = await ReadyAsync(); var view = await ViewAsync(await CountAsync(id));
        var responses = await Task.WhenAll(ApproveAsync(id, view.OperationId), CountAsync(id, Counts(9)));
        Assert.Equal(HttpStatusCode.OK, responses[0].StatusCode);
        Assert.Contains(responses[1].StatusCode, new[] { HttpStatusCode.Created, HttpStatusCode.Conflict });
        if (responses[1].StatusCode == HttpStatusCode.Created)
        {
            var next = await ViewAsync(responses[1]);
            Assert.Equal(ReconciliationStatus.Completed, next.Status); Assert.Equal(0m, next.TotalDifferenceTry);
        }
        else await CodeAsync(responses[1], HttpStatusCode.Conflict, "RECONCILIATION_APPROVAL_PENDING");
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CashManagementDbContext>();
        Assert.Equal(ReconciliationStatus.Approved, (await db.Reconciliations.SingleAsync(r => r.Id == view.OperationId)).Status);
        Assert.Single(await db.Movements.Where(m => m.Type == MovementType.ReconciliationAdjustment).ToArrayAsync());
        Assert.Equal(HttpStatusCode.OK, (await StateAsync(id, "IN_SERVICE")).StatusCode);
    }

    [Fact]
    public async Task Approval_database_failure_keeps_pending_and_blocks_service()
    {
        var id = await ReadyAsync(); var view = await ViewAsync(await CountAsync(id));
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CashManagementDbContext>();
        await db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION cash.reject_approval() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN IF NEW."Status" = 'Approved' THEN RAISE EXCEPTION 'forced approval failure'; END IF; RETURN NEW; END $$;
            CREATE TRIGGER reject_approval BEFORE UPDATE ON cash.reconciliations FOR EACH ROW EXECUTE FUNCTION cash.reject_approval();
            """);
        var failure = await ApproveAsync(id, view.OperationId);
        Assert.Equal(HttpStatusCode.InternalServerError, failure.StatusCode);
        Assert.DoesNotContain("forced approval failure", await failure.Content.ReadAsStringAsync());
        var saved = await db.Reconciliations.AsNoTracking().SingleAsync();
        Assert.Equal(ReconciliationStatus.AwaitingApproval, saved.Status); Assert.Null(saved.ApprovedBy); Assert.Null(saved.ApprovedAt);
        await CodeAsync(await StateAsync(id, "IN_SERVICE"), HttpStatusCode.Conflict, "RECONCILIATION_APPROVAL_PENDING");
        await db.Database.ExecuteSqlRawAsync("DROP TRIGGER reject_approval ON cash.reconciliations; DROP FUNCTION cash.reject_approval();");
        Assert.Equal(HttpStatusCode.OK, (await ApproveAsync(id, view.OperationId)).StatusCode);
    }

    [Fact]
    public async Task Missing_rate_rolls_back_count_and_invalid_rate_or_review_is_rejected_by_database()
    {
        var id = await ReadyAsync();
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CashManagementDbContext>();
        await Assert.ThrowsAsync<Npgsql.PostgresException>(() => db.Database.ExecuteSqlRawAsync("UPDATE cash.exchange_rates SET \"RateToTry\" = 0 WHERE \"Currency\" = 'USD';"));
        await db.Database.ExecuteSqlRawAsync("DELETE FROM cash.exchange_rates WHERE \"Currency\" = 'EUR';");
        await CodeAsync(await CountAsync(id), HttpStatusCode.Conflict, "INVALID_EXCHANGE_RATES");
        Assert.Empty(await db.Reconciliations.ToArrayAsync());
        Assert.Empty(await db.Movements.Where(m => m.Type == MovementType.ReconciliationAdjustment).ToArrayAsync());
        Assert.Equal(20, Assert.Single((await Client.GetFromJsonAsync<ConsistencyReport>($"/api/atms/{id}/consistency", Json))!.Lines).StoredOnHand);
        await db.Database.ExecuteSqlRawAsync("INSERT INTO cash.exchange_rates (\"Currency\", \"RateToTry\") VALUES ('EUR',45);");
        var view = await ViewAsync(await CountAsync(id));
        await Assert.ThrowsAsync<Npgsql.PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE cash.reconciliations SET \"Status\" = 'Completed' WHERE \"Id\" = {view.OperationId}"));
    }

    [Fact]
    public async Task Migration_preserves_pre_v080_history_as_legacy_without_inventing_rates_or_blocking_service()
    {
        var id = await ReadyAsync(); var legacyId = Guid.NewGuid();
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CashManagementDbContext>();
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20261012111736_AddReconciliations");
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO cash.reconciliations (\"Id\",\"AtmId\",\"Actor\",\"OccurredAt\",\"LinesJson\") VALUES ({legacyId},{id},{"Old User"},{DateTimeOffset.UtcNow},'[]'::jsonb)");
        await migrator.MigrateAsync();
        var view = (await Client.GetFromJsonAsync<ReconciliationView>($"/api/atms/{id}/reconciliations/{legacyId}", Json))!;
        Assert.Equal(ReconciliationStatus.LegacyCompleted, view.Status);
        Assert.Equal("Old User", view.Actor);
        Assert.Null(view.TotalDifferenceTry); Assert.Null(view.ExchangeRates);
        await CodeAsync(await ApproveAsync(id, legacyId), HttpStatusCode.Conflict, "APPROVAL_NOT_REQUIRED");
        Assert.Equal(HttpStatusCode.OK, (await StateAsync(id, "IN_SERVICE")).StatusCode);
        Assert.Equal(3, (await Client.GetFromJsonAsync<ExchangeRateSnapshot[]>("/api/exchange-rates", Json))!.Length);
    }
}
