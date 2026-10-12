using FimpleCaseStudy.Domain.Cash;

namespace FimpleCaseStudy.Application.Atms;

public sealed record CreateAtmRequest(string? Name);
public sealed record ConfigureAtmRequest(IReadOnlyList<CassetteConfiguration>? Cassettes);
public sealed record CashRequest(IReadOnlyList<CashLine>? Lines);
public sealed record TransitionAtmRequest(AtmState State);
public sealed record BalanceView(Currency Currency, int Denomination, int OnHand, int Reserved, int Available);
public sealed record CassetteView(Guid Id, int Slot, CassetteKind Kind, Currency? Currency,
    int? Denomination, int Capacity, int OnHand, int Reserved, int Available, IReadOnlyList<BalanceView> Balances);
public sealed record AtmSummary(Guid Id, string Name, AtmState State, DateTimeOffset CreatedAt);
public sealed record AtmView(Guid Id, string Name, AtmState State, IReadOnlyList<CassetteView> Cassettes)
{
    public static AtmView From(Atm atm) => new(atm.Id, atm.Name, atm.State,
        atm.Cassettes.OrderBy(x => x.Slot).Select(c => new CassetteView(
            c.Id, c.Slot, c.Kind, c.Currency, c.Denomination, Cassette.Capacity,
            c.OnHand, c.Reserved, c.Available,
            c.Balances.OrderBy(b => b.Currency).ThenBy(b => b.Denomination)
                .Select(b => new BalanceView(b.Currency, b.Denomination, b.OnHand, b.Reserved, b.Available))
                .ToArray())).ToArray());
}
