namespace FimpleCaseStudy.Domain.Cash;

public sealed class Cassette
{
    public const int Capacity = 100;
    private readonly List<CassetteBalance> _balances = [];
    private Cassette() { }

    internal Cassette(Guid atmId, int slot)
    {
        Id = Guid.NewGuid();
        AtmId = atmId;
        Slot = slot;
        Kind = slot == 5 ? CassetteKind.Deposit : CassetteKind.Recycling;
    }

    public Guid Id { get; private set; }
    public Guid AtmId { get; private set; }
    public int Slot { get; private set; }
    public CassetteKind Kind { get; private set; }
    public Currency? Currency { get; private set; }
    public int? Denomination { get; private set; }
    public IReadOnlyCollection<CassetteBalance> Balances => _balances.AsReadOnly();
    public int OnHand => _balances.Sum(x => x.OnHand);
    public int Reserved => _balances.Sum(x => x.Reserved);
    public int Available => OnHand - Reserved;

    internal bool ConfigurationChanges(CassetteConfiguration config) =>
        Currency != config.Currency || Denomination != config.Denomination;

    internal void Configure(CassetteConfiguration config)
    {
        Currency = config.Currency;
        Denomination = config.Denomination;
    }

    internal void ValidateMatchingNote(CashLine line)
    {
        if (Kind == CassetteKind.Recycling &&
            (Currency != line.Currency || Denomination != line.Denomination))
            throw new DomainException("CASSETTE_NOTE_MISMATCH", "The note does not match the recycling cassette.");
    }

    internal void ChangePhysicalCount(CashLine line, int delta)
    {
        var balance = _balances.SingleOrDefault(x =>
            x.Currency == line.Currency && x.Denomination == line.Denomination);
        if (balance is null)
        {
            balance = new CassetteBalance(Id, line.Currency, line.Denomination);
            _balances.Add(balance);
        }
        balance.ChangePhysicalCount(delta);
    }
}
