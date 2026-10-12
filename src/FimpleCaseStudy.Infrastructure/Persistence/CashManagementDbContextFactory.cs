using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace FimpleCaseStudy.Infrastructure.Persistence;

// Used only by dotnet-ef; constructing the model does not connect to PostgreSQL.
public sealed class CashManagementDbContextFactory : IDesignTimeDbContextFactory<CashManagementDbContext>
{
    public CashManagementDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Database")
            ?? "Host=localhost;Database=cash_management;Username=cash_app;Password=local-development-only";
        var options = new DbContextOptionsBuilder<CashManagementDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        return new CashManagementDbContext(options);
    }
}
