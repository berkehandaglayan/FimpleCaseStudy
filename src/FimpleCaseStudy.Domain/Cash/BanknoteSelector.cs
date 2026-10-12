namespace FimpleCaseStudy.Domain.Cash;

// A snapshot of one recycling cassette; selection never mutates physical or reserved cash.
public sealed record RecyclingStock(Guid CassetteId, int Slot, Currency Currency,
    int Denomination, int OnHand, int Reserved)
{
    public int Available => OnHand - Reserved;
}

public sealed record DispenseLine(Guid CassetteId, int Slot, int Denomination, int Count);
public sealed record DispensePlan(int Amount, IReadOnlyList<DispenseLine> Lines)
{
    public int NoteCount => Lines.Sum(x => x.Count);
}
public sealed record BanknoteSelection(DispensePlan? Exact, DispensePlan? Lower, DispensePlan? Upper);

public static class BanknoteSelector
{
    // All supported denominations are divisible by 10. Amounts need not be.
    private const int Scale = 10;

    public static BanknoteSelection Select(Atm atm, Currency currency, int amount, int remainingLimit)
    {
        if (atm.State != AtmState.InService)
            throw new DomainException("INVALID_ATM_STATE", "Selection requires an in-service ATM.", ErrorKind.Conflict);
        return Select(atm.Cassettes.Where(c => c.Kind == CassetteKind.Recycling)
            .Select(c => new RecyclingStock(c.Id, c.Slot, c.Currency!.Value,
                c.Denomination!.Value, c.OnHand, c.Reserved)).ToArray(), currency, amount, remainingLimit);
    }

    public static BanknoteSelection Select(IReadOnlyList<RecyclingStock> stock,
        Currency currency, int amount, int remainingLimit)
    {
        Banknotes.Validate(currency, 20);
        if (amount <= 0 || remainingLimit < 0)
            throw new DomainException("INVALID_WITHDRAWAL_AMOUNT", "Amount must be positive and remaining limit nonnegative.");
        if (amount > remainingLimit)
            throw new DomainException("DAILY_LIMIT_EXCEEDED", "Requested amount exceeds the remaining daily limit.", ErrorKind.Conflict);
        if (stock.Count > 4 || stock.Any(s => s is null) ||
            stock.Select(s => s.CassetteId).Distinct().Count() != stock.Count ||
            stock.Select(s => s.Slot).Distinct().Count() != stock.Count)
            throw new DomainException("INVALID_SELECTION_STOCK", "Provide at most four distinct recycling cassettes.");
        foreach (var s in stock)
        {
            Banknotes.Validate(s.Currency, s.Denomination);
            if (s.CassetteId == Guid.Empty || s.Slot is < 1 or > 4 ||
                s.OnHand is < 0 or > Cassette.Capacity || s.Reserved < 0 || s.Reserved > s.OnHand)
                throw new DomainException("INVALID_SELECTION_STOCK", "Invalid cassette identity, slot or stock counts.");
        }

        var eligible = stock.Where(s => s.Currency == currency && s.Available > 0).ToArray();
        var groups = eligible.GroupBy(s => s.Denomination).OrderBy(g => g.Key)
            .Select(g => (Denomination: g.Key, Count: g.Sum(s => s.Available))).ToArray();
        // At most 4 * 100 * 500 / 10 = 20,000 cells per stage, even for int.MaxValue requests.
        var bound = Math.Min(remainingLimit, groups.Sum(g => g.Denomination * g.Count)) / Scale;
        var best = new int[groups.Length + 1, bound + 1];
        var chosen = new int[groups.Length + 1, bound + 1];
        for (var stage = 0; stage <= groups.Length; stage++)
            for (var value = 0; value <= bound; value++) best[stage, value] = int.MaxValue;
        best[0, 0] = 0;

        for (var stage = 1; stage <= groups.Length; stage++)
        {
            var group = groups[stage - 1];
            var unit = group.Denomination / Scale;
            for (var value = 0; value <= bound; value++)
            {
                // On equal note counts, prefer more of the current (larger) denomination.
                for (var count = Math.Min(group.Count, value / unit); count >= 0; count--)
                {
                    var previous = best[stage - 1, value - count * unit];
                    if (previous == int.MaxValue || previous + count >= best[stage, value]) continue;
                    best[stage, value] = previous + count;
                    chosen[stage, value] = count;
                }
            }
        }

        DispensePlan Build(int value)
        {
            var total = value * Scale;
            var lines = new List<DispenseLine>();
            for (var stage = groups.Length; stage > 0; stage--)
            {
                var denomination = groups[stage - 1].Denomination;
                var count = chosen[stage, value];
                value -= count * (denomination / Scale);
                // Capacity is 100 for every cassette, so OnHand orders physical fullness ratios.
                foreach (var cassette in eligible.Where(s => s.Denomination == denomination)
                    .OrderByDescending(s => s.OnHand).ThenBy(s => s.Slot))
                {
                    var take = Math.Min(count, cassette.Available);
                    if (take > 0) lines.Add(new(cassette.CassetteId, cassette.Slot, denomination, take));
                    count -= take;
                }
            }
            return new(total, lines.AsReadOnly());
        }

        if (amount % Scale == 0 && amount / Scale <= bound && best[groups.Length, amount / Scale] != int.MaxValue)
            return new(Build(amount / Scale), null, null);

        int? lower = null, upper = null;
        for (var value = 1; value <= bound; value++)
        {
            if (best[groups.Length, value] == int.MaxValue) continue;
            if (value * Scale < amount) lower = value;
            else if (value * Scale > amount) { upper = value; break; }
        }
        return new(null, lower.HasValue ? Build(lower.Value) : null, upper.HasValue ? Build(upper.Value) : null);
    }
}
