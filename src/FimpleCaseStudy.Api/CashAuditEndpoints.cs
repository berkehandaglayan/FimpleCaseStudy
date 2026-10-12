using FimpleCaseStudy.Application.Atms;
using FimpleCaseStudy.Domain.Cash;
using FimpleCaseStudy.Domain;

namespace FimpleCaseStudy.Api;

public static class CashAuditEndpoints
{
    public static void MapCashAuditEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/atms/{id:guid}").WithTags("Cash audit");
        app.MapGet("/api/exchange-rates", async (ICashAuditService service, CancellationToken ct) =>
            Results.Ok(await service.GetExchangeRatesAsync(ct)))
            .WithTags("Cash audit").Produces<IReadOnlyList<ExchangeRateSnapshot>>();
        group.MapPost("/reconciliations", async (Guid id, ReconciliationRequest request, HttpContext context,
            ICashAuditService service, CancellationToken ct) =>
        {
            var actor = Actor(context, required: false);
            var receipt = await service.ReconcileAsync(id, request, actor, ct);
            return Results.Created($"/api/atms/{id}/reconciliations/{receipt.OperationId}", receipt);
        }).Produces<ReconciliationView>(201).ProducesProblem(400).ProducesProblem(404).ProducesProblem(409);
        group.MapGet("/reconciliations/{operationId:guid}", async (Guid id, Guid operationId,
            ICashAuditService service, CancellationToken ct) => Results.Ok(await service.GetReconciliationAsync(id, operationId, ct)))
            .Produces<ReconciliationView>().ProducesProblem(404);
        group.MapPost("/reconciliations/{operationId:guid}/approve", async (Guid id, Guid operationId,
            HttpContext context, ICashAuditService service, CancellationToken ct) =>
            Results.Ok(await service.ApproveAsync(id, operationId, Actor(context, required: true), ct)))
            .Produces<ReconciliationView>().ProducesProblem(400).ProducesProblem(404).ProducesProblem(409);
        group.MapGet("/report", async (Guid id, ICashAuditService service, CancellationToken ct) =>
            Results.Ok(await service.ReportAsync(id, ct))).Produces<AtmCashReport>().ProducesProblem(404).ProducesProblem(409);
        group.MapGet("/consistency", async (Guid id, ICashAuditService service, CancellationToken ct) =>
            Results.Ok(await service.CheckConsistencyAsync(id, ct))).Produces<ConsistencyReport>().ProducesProblem(404);
        group.MapGet("/alerts", async (Guid id, int? limit, int? offset, ICashAuditService service, CancellationToken ct) =>
            Results.Ok(await service.GetAlertsAsync(id, limit ?? 50, offset ?? 0, ct)))
            .Produces<IReadOnlyList<LowCashAlertView>>().ProducesProblem(400).ProducesProblem(404);
    }

    private static string Actor(HttpContext context, bool required)
    {
        if (!context.Request.Headers.TryGetValue("X-User", out var user))
        {
            if (required) throw new DomainException("ACTOR_REQUIRED", "X-User is required for approval.");
            return "system";
        }
        if (user.Count != 1) throw new DomainException("INVALID_ACTOR", "Provide exactly one X-User value.");
        return user.ToString();
    }
}
