using Microsoft.EntityFrameworkCore;
using FimpleCaseStudy.Domain.Cash;

namespace FimpleCaseStudy.Infrastructure.Persistence;

public sealed class CashManagementDbContext(DbContextOptions<CashManagementDbContext> options, TimeProvider? clock = null)
    : DbContext(options)
{
    public DbSet<Atm> Atms => Set<Atm>();
    public DbSet<CashMovement> Movements => Set<CashMovement>();
    public DbSet<WithdrawalQuote> WithdrawalQuotes => Set<WithdrawalQuote>();
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();
    public DbSet<ReconciliationRecord> Reconciliations => Set<ReconciliationRecord>();
    public DbSet<ExchangeRate> ExchangeRates => Set<ExchangeRate>();
    public DbSet<LowCashAlert> LowCashAlerts => Set<LowCashAlert>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("cash");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CashManagementDbContext).Assembly);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        GuardMovementImmutability();
        CaptureLowCashAlerts();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        GuardMovementImmutability();
        CaptureLowCashAlerts();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void GuardMovementImmutability()
    {
        if (ChangeTracker.Entries<CashMovement>().Any(e => e.State is EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("Cash movement records cannot be modified or deleted.");
    }

    private void CaptureLowCashAlerts()
    {
        // Original tracked counts and final aggregate counts compare the net change at this save boundary.
        // A confirm's release+withdrawal cannot produce a false intermediate threshold crossing.
        var movements = ChangeTracker.Entries<CashMovement>().Where(e => e.State == EntityState.Added)
            .Select(e => e.Entity).ToArray();
        var now = new DateTimeOffset((clock ?? TimeProvider.System).GetUtcNow().UtcTicks / 10 * 10, TimeSpan.Zero);
        foreach (var atmEntry in ChangeTracker.Entries<Atm>().ToArray())
        {
            var atm = atmEntry.Entity;
            var firstService = atmEntry.State != EntityState.Added &&
                atmEntry.OriginalValues.GetValue<AtmState>(nameof(Atm.State)) == AtmState.Loaded && atm.State == AtmState.InService;
            var serviceOperation = Guid.NewGuid();
            foreach (var cassette in atm.Cassettes.Where(c => c.Kind == CassetteKind.Recycling && c.Currency is not null))
            {
                var before = cassette.Balances.Sum(b =>
                {
                    var entry = Entry(b);
                    return entry.State == EntityState.Added ? 0 :
                        entry.OriginalValues.GetValue<int>(nameof(CassetteBalance.OnHand)) -
                        entry.OriginalValues.GetValue<int>(nameof(CassetteBalance.Reserved));
                });
                if (!LowCashPolicy.ShouldAlert(cassette.Kind, before, cassette.Available, firstService)) continue;
                var operation = movements.LastOrDefault(m => m.CassetteId == cassette.Id)?.OperationId ?? serviceOperation;
                if (ChangeTracker.Entries<LowCashAlert>().Any(e => e.State == EntityState.Added &&
                        e.Entity.OperationId == operation && e.Entity.CassetteId == cassette.Id)) continue;
                LowCashAlerts.Add(LowCashAlert.Create(atm, cassette, before, operation, firstService, now));
            }
        }
    }
}
