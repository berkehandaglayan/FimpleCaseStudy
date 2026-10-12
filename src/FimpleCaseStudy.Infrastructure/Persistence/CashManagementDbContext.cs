using Microsoft.EntityFrameworkCore;
using FimpleCaseStudy.Domain.Cash;

namespace FimpleCaseStudy.Infrastructure.Persistence;

public sealed class CashManagementDbContext(DbContextOptions<CashManagementDbContext> options)
    : DbContext(options)
{
    public DbSet<Atm> Atms => Set<Atm>();
    public DbSet<CashMovement> Movements => Set<CashMovement>();
    public DbSet<WithdrawalQuote> WithdrawalQuotes => Set<WithdrawalQuote>();
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();
    public DbSet<ReconciliationRecord> Reconciliations => Set<ReconciliationRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("cash");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CashManagementDbContext).Assembly);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        GuardMovementImmutability();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        GuardMovementImmutability();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void GuardMovementImmutability()
    {
        if (ChangeTracker.Entries<CashMovement>().Any(e => e.State is EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("Cash movement records cannot be modified or deleted.");
    }
}
