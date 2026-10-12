using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using FimpleCaseStudy.Api;
using FimpleCaseStudy.Application;
using FimpleCaseStudy.Infrastructure;
using FimpleCaseStudy.Infrastructure.Persistence;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("Database")
    ?? throw new InvalidOperationException("ConnectionStrings:Database is required.");

builder.Services.AddApplication();
builder.Services.AddInfrastructure(connectionString);
builder.Services.AddSingleton(new CardIdentity(builder.Configuration["Withdrawals:HmacKey"]
    ?? throw new InvalidOperationException("Withdrawals:HmacKey is required.")));
builder.Services.AddHostedService<QuoteExpiryWorker>();
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseUpper, allowIntegerValues: false)));
builder.Services.AddExceptionHandler<DomainExceptionHandler>();
builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
{
    context.ProblemDetails.Instance = context.HttpContext.Request.Path;
    context.ProblemDetails.Extensions["traceId"] =
        Activity.Current?.Id ?? context.HttpContext.TraceIdentifier;
    context.ProblemDetails.Extensions.TryAdd("code", context.ProblemDetails.Status switch
    {
        400 => "INVALID_REQUEST",
        404 => "RESOURCE_NOT_FOUND",
        405 => "METHOD_NOT_ALLOWED",
        503 => "SERVICE_UNAVAILABLE",
        _ => "INTERNAL_ERROR"
    });
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseSwagger();
app.UseSwaggerUI();

await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<CashManagementDbContext>();
    await db.Database.MigrateAsync();
}

app.MapGet("/api/info", () => Results.Ok(new
{
    name = "ATM Cash Management API",
    version = "0.6.0"
})).WithName("GetApiInfo").WithTags("System");

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false
});
app.MapHealthChecks("/health", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
    ResponseWriter = async (context, report) =>
    {
        if (report.Status == HealthStatus.Healthy)
        {
            await context.Response.WriteAsJsonAsync(new { status = "Healthy" });
            return;
        }

        await Results.Problem(
            statusCode: StatusCodes.Status503ServiceUnavailable,
            title: "Service unavailable",
            detail: "The database is unavailable or migrations are pending.",
            extensions: new Dictionary<string, object?> { ["code"] = "DATABASE_NOT_READY" })
            .ExecuteAsync(context);
    }
});

app.MapAtmEndpoints();
app.MapWithdrawalEndpoints();
app.Run();

public partial class Program;
