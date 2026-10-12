namespace FimpleCaseStudy.Domain.Cash;

public sealed class Atm
{
    private readonly List<Cassette> _cassettes = [];
    private Atm() { }

    public Guid Id { get; private set; }
    public string Name { get; private set; } = null!;
    public AtmState State { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public IReadOnlyCollection<Cassette> Cassettes => _cassettes.AsReadOnly();

    public static Atm Create(string? name, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 100)
            throw new DomainException("INVALID_ATM_NAME", "ATM name must contain 1 to 100 characters.");
        var atm = new Atm { Id = Guid.NewGuid(), Name = name.Trim(), CreatedAt = now.ToUniversalTime() };
        for (var slot = 1; slot <= 5; slot++) atm._cassettes.Add(new Cassette(atm.Id, slot));
        return atm;
    }

    public void Configure(IReadOnlyList<CassetteConfiguration>? configurations)
    {
        RequireState(AtmState.Unconfigured, AtmState.Configured, AtmState.Maintenance);
        if (configurations is null || configurations.Count != 4 ||
            configurations.Any(x => x is null) ||
            !configurations.Select(x => x.Slot).Order().SequenceEqual(new[] { 1, 2, 3, 4 }))
            throw new DomainException("INVALID_CASSETTE_CONFIGURATION", "Configure recycling slots 1, 2, 3 and 4 exactly once.");

        foreach (var config in configurations) Banknotes.Validate(config.Currency, config.Denomination);
        var foreign = configurations.Count(x => x.Currency != Currency.TRY);
        if (foreign is < 1 or > 2)
            throw new DomainException("INVALID_FOREIGN_CASSETTE_COUNT", "One or two recycling cassettes must use USD or EUR.");

        // Validate every change before modifying the aggregate.
        foreach (var config in configurations)
        {
            var cassette = _cassettes.Single(x => x.Slot == config.Slot);
            if (cassette.ConfigurationChanges(config) && (cassette.OnHand != 0 || cassette.Reserved != 0))
                throw new DomainException("CASSETTE_NOT_EMPTY", "Unload the cassette before changing its configuration.", ErrorKind.Conflict);
        }
        foreach (var config in configurations)
            _cassettes.Single(x => x.Slot == config.Slot).Configure(config);

        if (State == AtmState.Unconfigured) State = AtmState.Configured;
    }

    public IReadOnlyList<CashMovement> Load(IReadOnlyList<CashLine>? lines, string actor, DateTimeOffset now)
        => ChangeCash(lines, actor, now, loading: true);

    public IReadOnlyList<CashMovement> Unload(IReadOnlyList<CashLine>? lines, string actor, DateTimeOffset now)
        => ChangeCash(lines, actor, now, loading: false);

    public DepositChange Deposit(IReadOnlyList<DepositedNote>? notes, DateTimeOffset now)
    {
        RequireState(AtmState.InService);
        if (notes is null || notes.Count is < 1 or > 100 || notes.Any(n => n is null ||
                n.Count is < 1 or > 10_000 || n.Currency?.Length > 32) ||
            notes.Sum(n => (long)n.Count) > 10_000)
            throw new DomainException("INVALID_DEPOSIT_NOTES", "Provide 1-100 note groups and at most 10000 positive-count notes.");

        var placements = new List<DepositPlacement>();
        var returned = new List<ReturnedNote>();
        var planned = _cassettes.ToDictionary(c => c.Slot, _ => 0);
        var deposit = _cassettes.Single(c => c.Kind == CassetteKind.Deposit);
        for (var index = 0; index < notes.Count; index++)
        {
            var note = notes[index];
            Currency? currency = note.Currency?.Trim().ToUpperInvariant() switch
            {
                "TRY" => Currency.TRY, "USD" => Currency.USD, "EUR" => Currency.EUR, _ => null
            };
            if (!note.Recognized || currency is null || note.Denomination is not (20 or 50 or 100 or 500))
            {
                returned.Add(new(index, note.Currency, note.Denomination, note.Count, "UNRECOGNIZED"));
                continue;
            }
            var remaining = note.Count;
            // Physical cash, including reservations, determines remaining capacity.
            var targets = _cassettes.Where(c => c.Kind == CassetteKind.Recycling &&
                    c.Currency == currency && c.Denomination == note.Denomination)
                .OrderBy(c => c.OnHand + planned[c.Slot]).ThenBy(c => c.Slot).Append(deposit);
            foreach (var cassette in targets)
            {
                var take = Math.Min(remaining, Cassette.Capacity - cassette.OnHand - planned[cassette.Slot]);
                if (take > 0)
                {
                    placements.Add(new(index, cassette.Id, cassette.Slot, currency.Value, note.Denomination.Value, take));
                    planned[cassette.Slot] += take;
                    remaining -= take;
                }
                if (remaining == 0) break;
            }
            if (remaining > 0)
                returned.Add(new(index, note.Currency, note.Denomination, remaining, "CAPACITY_EXCEEDED"));
        }

        var operationId = Guid.NewGuid();
        var movements = new List<CashMovement>();
        // Apply only after validation/planning. Duplicate input groups share cassette capacity.
        foreach (var group in placements.GroupBy(p => (p.Slot, p.Currency, p.Denomination)))
        {
            var cassette = _cassettes.Single(c => c.Slot == group.Key.Slot);
            var line = new CashLine(cassette.Slot, group.Key.Currency, group.Key.Denomination, group.Sum(p => p.Count));
            cassette.ChangePhysicalCount(line, line.Count);
            movements.Add(new CashMovement(Id, cassette.Id, line, line.Count, MovementType.Deposit,
                operationId, now.ToUniversalTime(), "system"));
        }
        return new(new(operationId, placements.AsReadOnly(), returned.AsReadOnly()), movements.AsReadOnly());
    }

    public void TransitionTo(AtmState target)
    {
        var allowed = (State, target) is
            (AtmState.Loaded, AtmState.InService) or
            (AtmState.InService, AtmState.Maintenance) or
            (AtmState.Maintenance, AtmState.InService);
        if (!allowed)
            throw new DomainException("INVALID_STATE_TRANSITION", $"Cannot transition from {State} to {target}.", ErrorKind.Conflict);
        State = target;
    }

    public IReadOnlyList<CashMovement> ChangeWithdrawalCash(Currency currency,
        IReadOnlyList<DispenseLine> reservations, DispensePlan? withdrawal,
        bool release, Guid operationId, DateTimeOffset now)
    {
        var movements = new List<CashMovement>();
        Banknotes.Validate(currency, 20);
        if (!release || withdrawal is not null) RequireState(AtmState.InService);
        if (operationId == Guid.Empty || reservations is null || reservations.Count is < 1 or > 4 ||
            reservations.Any(l => l is null) || reservations.Select(l => l.CassetteId).Distinct().Count() != reservations.Count ||
            (withdrawal is not null && !release))
            throw new DomainException("INVALID_RESERVATION", "Provide distinct recycling reservation lines.");
        foreach (var line in reservations)
        {
            var cassette = _cassettes.SingleOrDefault(c => c.Id == line.CassetteId);
            var balance = cassette?.Balances.SingleOrDefault(b => b.Currency == currency && b.Denomination == line.Denomination);
            if (cassette is null || cassette.Kind != CassetteKind.Recycling || cassette.Slot != line.Slot ||
                cassette.Currency != currency || cassette.Denomination != line.Denomination || balance is null ||
                line.Count <= 0 || (release ? balance.Reserved : balance.Available) < line.Count)
                throw new DomainException("INVALID_RESERVATION", "Reservation does not match available cash.", ErrorKind.Conflict);
        }
        if (withdrawal is not null)
        {
            if (withdrawal.Lines is null || withdrawal.Lines.Count is < 1 or > 4 || withdrawal.Lines.Any(l => l is null) ||
                withdrawal.Lines.Select(l => l.CassetteId).Distinct().Count() != withdrawal.Lines.Count ||
                withdrawal.Amount <= 0 || withdrawal.Lines.Sum(l => (long)l.Denomination * l.Count) != withdrawal.Amount)
                throw new DomainException("INVALID_WITHDRAWAL_PLAN", "Invalid withdrawal plan.");
            foreach (var line in withdrawal.Lines)
            {
                var reservation = reservations.SingleOrDefault(r => r.CassetteId == line.CassetteId);
                if (reservation is null || line.Count <= 0 || line.Count > reservation.Count ||
                    line.Slot != reservation.Slot || line.Denomination != reservation.Denomination)
                    throw new DomainException("INVALID_WITHDRAWAL_PLAN", "Withdrawal must be covered by the reservation.");
            }
        }
        foreach (var line in reservations)
        {
            var cassette = _cassettes.Single(c => c.Id == line.CassetteId);
            var balance = cassette.Balances.Single(b => b.Currency == currency && b.Denomination == line.Denomination);
            var delta = release ? -line.Count : line.Count;
            balance.ChangeReservation(delta);
            movements.Add(new CashMovement(Id, cassette.Id, new CashLine(line.Slot, currency, line.Denomination, line.Count),
                0, release ? MovementType.ReservationRelease : MovementType.Reservation, operationId, now, "system", delta));
        }
        if (withdrawal is not null)
            foreach (var line in withdrawal.Lines)
            {
                var cassette = _cassettes.Single(c => c.Id == line.CassetteId);
                var cashLine = new CashLine(line.Slot, currency, line.Denomination, line.Count);
                cassette.ChangePhysicalCount(cashLine, -line.Count);
                movements.Add(new CashMovement(Id, cassette.Id, cashLine, -line.Count,
                    MovementType.Withdrawal, operationId, now, "system"));
            }
        return movements;
    }

    private IReadOnlyList<CashMovement> ChangeCash(
        IReadOnlyList<CashLine>? lines, string actor, DateTimeOffset now, bool loading)
    {
        RequireState(AtmState.Configured, AtmState.Maintenance);
        if (string.IsNullOrWhiteSpace(actor) || actor.Length > 128)
            throw new DomainException("INVALID_ACTOR", "Operator identity must contain 1 to 128 characters.");
        if (lines is null || lines.Count is < 1 or > 60 || lines.Any(x => x is null))
            throw new DomainException("INVALID_CASH_LINES", "Provide between 1 and 60 note lines.");
        if (lines.Select(x => (x.Slot, x.Currency, x.Denomination)).Distinct().Count() != lines.Count)
            throw new DomainException("DUPLICATE_CASH_LINE", "Each slot, currency and denomination must occur once.");

        foreach (var line in lines)
        {
            Banknotes.Validate(line.Currency, line.Denomination);
            if (line.Slot is < 1 or > 5 || line.Count is < 1 or > Cassette.Capacity)
                throw new DomainException("INVALID_CASH_LINE", "Slot must be 1-5 and count 1-100.");
            var cassette = _cassettes.Single(x => x.Slot == line.Slot);
            cassette.ValidateMatchingNote(line);
            if (!loading)
            {
                var available = cassette.Balances.SingleOrDefault(x =>
                    x.Currency == line.Currency && x.Denomination == line.Denomination)?.Available ?? 0;
                if (line.Count > available)
                    throw new DomainException("INSUFFICIENT_CASH", "The requested notes are not available.", ErrorKind.Conflict);
            }
        }
        if (loading)
        {
            foreach (var group in lines.GroupBy(x => x.Slot))
            {
                if (_cassettes.Single(x => x.Slot == group.Key).OnHand + group.Sum(x => x.Count) > Cassette.Capacity)
                    throw new DomainException("CASSETTE_CAPACITY_EXCEEDED", "A cassette can hold at most 100 notes.", ErrorKind.Conflict);
            }
            if (State == AtmState.Configured && !lines.Any(x => x.Slot != 5))
                throw new DomainException("INITIAL_RECYCLING_CASH_REQUIRED", "Initial loading must include cash in a recycling cassette.");
        }

        var operationId = Guid.NewGuid();
        var movements = new List<CashMovement>();
        foreach (var line in lines)
        {
            var cassette = _cassettes.Single(x => x.Slot == line.Slot);
            var delta = loading ? line.Count : -line.Count;
            cassette.ChangePhysicalCount(line, delta);
            movements.Add(new CashMovement(Id, cassette.Id, line, delta,
                loading ? MovementType.Load : MovementType.Unload, operationId, now.ToUniversalTime(), actor));
        }
        if (loading && State == AtmState.Configured) State = AtmState.Loaded;
        return movements;
    }

    private void RequireState(params AtmState[] allowed)
    {
        if (!allowed.Contains(State))
            throw new DomainException("INVALID_ATM_STATE", $"Operation is not allowed in {State}.", ErrorKind.Conflict);
    }
}
