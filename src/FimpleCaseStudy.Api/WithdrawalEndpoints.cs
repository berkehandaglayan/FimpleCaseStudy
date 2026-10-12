using FimpleCaseStudy.Application.Withdrawals;

namespace FimpleCaseStudy.Api;

public static class WithdrawalEndpoints
{
    public static void MapWithdrawalEndpoints(this WebApplication app)
    {
        var g = app.MapGroup("/api/atms/{atmId:guid}/withdrawals").WithTags("Withdrawals");
        g.MapPost("/quotes", async (Guid atmId, QuoteRequest request, IWithdrawalService service, CancellationToken ct) =>
        {
            var quote = await service.QuoteAsync(atmId, request, ct);
            return Results.Created($"/api/atms/{atmId}/withdrawals/quotes/{quote.Id}", quote);
        }).Produces<QuoteView>(201).ProducesProblem(400).ProducesProblem(404).ProducesProblem(409);
        g.MapGet("/quotes/{quoteId:guid}", async (Guid atmId, Guid quoteId, IWithdrawalService service, CancellationToken ct) =>
            Results.Ok(await service.GetAsync(atmId, quoteId, ct))).Produces<QuoteView>().ProducesProblem(404);
        g.MapPost("/quotes/{quoteId:guid}/confirm", async (Guid atmId, Guid quoteId, ConfirmRequest request,
            HttpContext context, IWithdrawalService service, CancellationToken ct) =>
        {
            var result = await service.ConfirmAsync(atmId, quoteId, request, context.Request.Headers["Idempotency-Key"].ToString(), ct);
            return Results.Content(result.Json, result.StatusCode == 200 ? "application/json" : "application/problem+json",
                statusCode: result.StatusCode);
        }).Produces<QuoteView>().ProducesProblem(400).ProducesProblem(404).ProducesProblem(409);
        g.MapPost("/quotes/{quoteId:guid}/cancel", async (Guid atmId, Guid quoteId, IWithdrawalService service, CancellationToken ct) =>
            Results.Ok(await service.CancelAsync(atmId, quoteId, ct))).Produces<QuoteView>().ProducesProblem(404).ProducesProblem(409);
    }
}
