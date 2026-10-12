using FimpleCaseStudy.Application.Atms;
using FimpleCaseStudy.Domain;
using FimpleCaseStudy.Domain.Cash;
using Microsoft.EntityFrameworkCore;

namespace FimpleCaseStudy.Infrastructure.Persistence;

internal sealed class AtmStore(CashManagementDbContext db, WithdrawalService withdrawals) : IAtmStore
{
    public async Task<Atm> CreateAsync(Atm atm, CancellationToken cancellationToken)
    {
        db.Atms.Add(atm);
        await db.SaveChangesAsync(cancellationToken);
        return atm;
    }

    public Task<Atm?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        db.Atms.AsNoTracking().Include(a => a.Cassettes).ThenInclude(c => c.Balances)
            .SingleOrDefaultAsync(a => a.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Atm>> ListAsync(int limit, int offset, CancellationToken cancellationToken) =>
        await db.Atms.AsNoTracking().OrderBy(a => a.CreatedAt).ThenBy(a => a.Id)
            .Skip(offset).Take(limit).ToArrayAsync(cancellationToken);

    public async Task<Atm> UpdateAsync(Guid id, Func<Atm, IReadOnlyList<CashMovement>> mutate,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await CashTransactionGate.AcquireAsync(db, cancellationToken);
        // All changes to an ATM use the same parent-row lock, including state changes.
        var atm = await db.Atms.FromSqlInterpolated(
            $"SELECT * FROM cash.atms WHERE \"Id\" = {id} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new DomainException("ATM_NOT_FOUND", "ATM does not exist.", ErrorKind.NotFound);
        await db.Entry(atm).Collection(a => a.Cassettes).Query().Include(c => c.Balances)
            .LoadAsync(cancellationToken);

        db.Movements.AddRange(mutate(atm));
        if (atm.State == AtmState.InService && await db.Reconciliations.AnyAsync(r =>
                r.AtmId == id && r.Status == ReconciliationStatus.AwaitingApproval, cancellationToken))
            throw new DomainException("RECONCILIATION_APPROVAL_PENDING", "A different operator must approve the reconciliation before returning to service.", ErrorKind.Conflict);
        if (atm.State == AtmState.Maintenance)
            await withdrawals.CancelAtmInsideAsync(id, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return atm;
    }
}
