using FimpleCaseStudy.Application.Atms;

namespace FimpleCaseStudy.Api;

public static class AtmEndpoints
{
    public static void MapAtmEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/atms").WithTags("ATMs");
        group.MapPost("/", async (CreateAtmRequest request, AtmService service, CancellationToken ct) =>
        {
            var atm = await service.CreateAsync(request, ct);
            return Results.Created($"/api/atms/{atm.Id}", atm);
        }).Produces<AtmView>(201).ProducesProblem(400);

        group.MapGet("/", async (int? limit, int? offset, AtmService service, CancellationToken ct) =>
            Results.Ok(await service.ListAsync(limit ?? 50, offset ?? 0, ct)))
            .Produces<IReadOnlyList<AtmSummary>>().ProducesProblem(400);

        group.MapGet("/{id:guid}", async (Guid id, AtmService service, CancellationToken ct) =>
            Results.Ok(await service.GetAsync(id, ct))).Produces<AtmView>().ProducesProblem(404);

        group.MapPut("/{id:guid}/configuration", async (Guid id, ConfigureAtmRequest request,
            AtmService service, CancellationToken ct) =>
            Results.Ok(await service.ConfigureAsync(id, request, ct)))
            .Produces<AtmView>().ProducesProblem(400).ProducesProblem(404).ProducesProblem(409);

        group.MapPost("/{id:guid}/load", async (Guid id, CashRequest request,
            HttpContext context, AtmService service, CancellationToken ct) =>
            Results.Ok(await service.LoadAsync(id, request, Actor(context), ct)))
            .Produces<AtmView>().ProducesProblem(400).ProducesProblem(404).ProducesProblem(409);

        group.MapPost("/{id:guid}/unload", async (Guid id, CashRequest request,
            HttpContext context, AtmService service, CancellationToken ct) =>
            Results.Ok(await service.UnloadAsync(id, request, Actor(context), ct)))
            .Produces<AtmView>().ProducesProblem(400).ProducesProblem(404).ProducesProblem(409);

        group.MapPost("/{id:guid}/state", async (Guid id, TransitionAtmRequest request,
            AtmService service, CancellationToken ct) =>
            Results.Ok(await service.TransitionAsync(id, request, ct)))
            .Produces<AtmView>().ProducesProblem(400).ProducesProblem(404).ProducesProblem(409);
    }

    private static string Actor(HttpContext context) =>
        context.Request.Headers.TryGetValue("X-User", out var user) ? user.ToString() : "system";
}
