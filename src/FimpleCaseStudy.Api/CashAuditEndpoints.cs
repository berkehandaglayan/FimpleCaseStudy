using FimpleCaseStudy.Application.Atms;
using FimpleCaseStudy.Domain.Cash;

namespace FimpleCaseStudy.Api;

public static class CashAuditEndpoints
{
    public static void MapCashAuditEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/atms/{id:guid}").WithTags("Cash audit");
        group.MapPost("/reconciliations", async (Guid id, ReconciliationRequest request, HttpContext context,
            ICashAuditService service, CancellationToken ct) =>
        {
            var actor = context.Request.Headers.TryGetValue("X-User", out var user) ? user.ToString() : "system";
            var receipt = await service.ReconcileAsync(id, request, actor, ct);
            return Results.Created($"/api/atms/{id}/reconciliations/{receipt.OperationId}", receipt);
        }).Produces<ReconciliationReceipt>(201).ProducesProblem(400).ProducesProblem(404).ProducesProblem(409);
        group.MapGet("/reconciliations/{operationId:guid}", async (Guid id, Guid operationId,
            ICashAuditService service, CancellationToken ct) => Results.Ok(await service.GetReconciliationAsync(id, operationId, ct)))
            .Produces<ReconciliationReceipt>().ProducesProblem(404);
        group.MapGet("/report", async (Guid id, ICashAuditService service, CancellationToken ct) =>
            Results.Ok(await service.ReportAsync(id, ct))).Produces<AtmCashReport>().ProducesProblem(404);
        group.MapGet("/consistency", async (Guid id, ICashAuditService service, CancellationToken ct) =>
            Results.Ok(await service.CheckConsistencyAsync(id, ct))).Produces<ConsistencyReport>().ProducesProblem(404);
    }
}
