using FimpleCaseStudy.Domain;
using FimpleCaseStudy.Domain.Cash;

namespace FimpleCaseStudy.Application.Atms;

public sealed class AtmService(IAtmStore store, TimeProvider clock)
{
    public async Task<AtmView> CreateAsync(CreateAtmRequest request, CancellationToken ct) =>
        AtmView.From(await store.CreateAsync(Atm.Create(request.Name, clock.GetUtcNow()), ct));

    public async Task<AtmView> GetAsync(Guid id, CancellationToken ct) =>
        AtmView.From(await store.FindAsync(id, ct)
            ?? throw new DomainException("ATM_NOT_FOUND", "ATM does not exist.", ErrorKind.NotFound));

    public async Task<IReadOnlyList<AtmSummary>> ListAsync(int limit, int offset, CancellationToken ct)
    {
        if (limit is < 1 or > 100 || offset < 0)
            throw new DomainException("INVALID_PAGINATION", "Limit must be 1-100 and offset must not be negative.");
        return (await store.ListAsync(limit, offset, ct))
            .Select(a => new AtmSummary(a.Id, a.Name, a.State, a.CreatedAt)).ToArray();
    }

    public async Task<AtmView> ConfigureAsync(Guid id, ConfigureAtmRequest request, CancellationToken ct) =>
        AtmView.From(await store.UpdateAsync(id, atm => { atm.Configure(request.Cassettes); return []; }, ct));

    public async Task<AtmView> LoadAsync(Guid id, CashRequest request, string actor, CancellationToken ct) =>
        AtmView.From(await store.UpdateAsync(id, atm => atm.Load(request.Lines, actor, clock.GetUtcNow()), ct));

    public async Task<AtmView> UnloadAsync(Guid id, CashRequest request, string actor, CancellationToken ct) =>
        AtmView.From(await store.UpdateAsync(id, atm => atm.Unload(request.Lines, actor, clock.GetUtcNow()), ct));

    public async Task<AtmView> TransitionAsync(Guid id, TransitionAtmRequest request, CancellationToken ct) =>
        AtmView.From(await store.UpdateAsync(id, atm => { atm.TransitionTo(request.State); return []; }, ct));
}
