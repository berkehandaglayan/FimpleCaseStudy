using FimpleCaseStudy.Infrastructure.Persistence;
using FimpleCaseStudy.Application.Atms;
using FimpleCaseStudy.Application.Withdrawals;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace FimpleCaseStudy.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, string connectionString)
    {
        services.AddScoped<IAtmStore, AtmStore>();
        services.AddScoped<ICashAuditService, CashAuditService>();
        services.AddScoped<WithdrawalService>();
        services.AddScoped<IWithdrawalService>(sp => sp.GetRequiredService<WithdrawalService>());
        services.AddDbContext<CashManagementDbContext>(options =>
            options.UseNpgsql(connectionString, postgres => postgres.CommandTimeout(15)));
        services.AddHealthChecks().AddCheck<DatabaseHealthCheck>(
            "database", failureStatus: HealthStatus.Unhealthy, tags: ["ready"]);
        return services;
    }
}
