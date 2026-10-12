using Microsoft.EntityFrameworkCore;

namespace FimpleCaseStudy.Infrastructure.Persistence;

internal static class CashTransactionGate
{
    // One cross-process lock, always before ATM rows. Case-study throughput tradeoff.
    public static Task AcquireAsync(CashManagementDbContext db, CancellationToken ct) =>
        db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(507050)", ct);
}
