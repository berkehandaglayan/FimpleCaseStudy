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
