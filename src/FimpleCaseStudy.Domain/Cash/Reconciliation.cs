namespace FimpleCaseStudy.Domain.Cash;

public sealed record CountedNote(Currency Currency, int Denomination, int Count);
public sealed record CassetteCount(int Slot, IReadOnlyList<CountedNote>? Notes);
public sealed record ReconciliationLine(Guid CassetteId, int Slot, Currency Currency,
    int Denomination, int StoredCount, int CountedCount, int Delta);
public sealed record ReconciliationReceipt(Guid OperationId, Guid AtmId, string Actor,
    DateTimeOffset OccurredAt, IReadOnlyList<ReconciliationLine> Lines);
public sealed record ReconciliationChange(ReconciliationReceipt Receipt, IReadOnlyList<CashMovement> Movements);

public sealed partial class Atm
{
    public ReconciliationChange Reconcile(IReadOnlyList<CassetteCount>? counts, string actor, DateTimeOffset now)
    {
        RequireState(AtmState.Maintenance);
        if (string.IsNullOrWhiteSpace(actor) || actor.Length > 128)
            throw new DomainException("INVALID_ACTOR", "Operator identity must contain 1 to 128 characters.");
        if (counts is null || counts.Count != 5 || counts.Any(c => c is null) ||
            !counts.Select(c => c.Slot).Order().SequenceEqual(new[] { 1, 2, 3, 4, 5 }))
            throw new DomainException("INVALID_CASSETTE_COUNTS", "Count all five cassette slots exactly once.");
        if (_cassettes.Any(c => c.Reserved != 0))
            throw new DomainException("ACTIVE_RESERVATIONS", "Release all reservations before counting cash.", ErrorKind.Conflict);

        // The request is a complete physical inventory, not a partial patch.
        foreach (var count in counts)
        {
            var cassette = _cassettes.Single(c => c.Slot == count.Slot);
            if (count.Notes is null || count.Notes.Count > 12 || count.Notes.Any(n => n is null) ||
                count.Notes.Select(n => (n.Currency, n.Denomination)).Distinct().Count() != count.Notes.Count)
                throw new DomainException("INVALID_COUNTED_NOTES", "Provide distinct currency/denomination counts; an empty deposit list means zero cash.");
            foreach (var note in count.Notes)
            {
                Banknotes.Validate(note.Currency, note.Denomination);
                if (note.Count is < 0 or > Cassette.Capacity)
                    throw new DomainException("INVALID_COUNTED_NOTES", "Count must be 0-100.");
                cassette.ValidateMatchingNote(new(count.Slot, note.Currency, note.Denomination, note.Count));
            }
            if (cassette.Kind == CassetteKind.Recycling && count.Notes.Count != 1)
                throw new DomainException("INVALID_COUNTED_NOTES", "Count the configured recycling note exactly once, including zero.");
            if (count.Notes.Sum(n => n.Count) > Cassette.Capacity)
                throw new DomainException("CASSETTE_CAPACITY_EXCEEDED", "A cassette can hold at most 100 notes.", ErrorKind.Conflict);
        }

        var lines = new List<ReconciliationLine>();
        foreach (var count in counts.OrderBy(c => c.Slot))
        {
            var cassette = _cassettes.Single(c => c.Slot == count.Slot);
            var keys = cassette.Balances.Select(b => (b.Currency, b.Denomination))
                .Union(count.Notes!.Select(n => (n.Currency, n.Denomination)))
                .OrderBy(k => k.Currency).ThenBy(k => k.Denomination);
            foreach (var key in keys)
            {
                var stored = cassette.Balances.SingleOrDefault(b => b.Currency == key.Currency && b.Denomination == key.Denomination)?.OnHand ?? 0;
                var counted = count.Notes!.SingleOrDefault(n => n.Currency == key.Currency && n.Denomination == key.Denomination)?.Count ?? 0;
                lines.Add(new(cassette.Id, cassette.Slot, key.Currency, key.Denomination, stored, counted, counted - stored));
            }
        }
        var operationId = Guid.NewGuid();
        var movements = new List<CashMovement>();
        foreach (var line in lines.Where(l => l.Delta != 0))
        {
            var cassette = _cassettes.Single(c => c.Id == line.CassetteId);
            var cash = new CashLine(line.Slot, line.Currency, line.Denomination, line.CountedCount);
            cassette.ChangePhysicalCount(cash, line.Delta);
            movements.Add(new(Id, cassette.Id, cash, line.Delta, MovementType.ReconciliationAdjustment,
                operationId, now.ToUniversalTime(), actor));
        }
        return new(new(operationId, Id, actor, now.ToUniversalTime(), lines.AsReadOnly()), movements.AsReadOnly());
    }
}
