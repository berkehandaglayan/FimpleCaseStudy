using FimpleCaseStudy.Domain.Cash;

namespace FimpleCaseStudy.Application.Atms;

public interface IAtmStore
{
    Task<Atm> CreateAsync(Atm atm, CancellationToken cancellationToken);
    Task<Atm?> FindAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<Atm>> ListAsync(int limit, int offset, CancellationToken cancellationToken);
    Task<Atm> UpdateAsync(Guid id, Func<Atm, IReadOnlyList<CashMovement>> mutate, CancellationToken cancellationToken);
}
