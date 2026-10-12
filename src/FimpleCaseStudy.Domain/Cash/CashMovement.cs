namespace FimpleCaseStudy.Domain.Cash;

public sealed class CashMovement
{
    private CashMovement() { }
    internal CashMovement(Guid atmId, Guid cassetteId, CashLine line, int physicalDelta,
        MovementType type, Guid operationId, DateTimeOffset occurredAt, string actor, int reservedDelta = 0)
    {
        Id = Guid.NewGuid();
        AtmId = atmId;
        CassetteId = cassetteId;
        Currency = line.Currency;
        Denomination = line.Denomination;
        PhysicalDelta = physicalDelta;
        ReservedDelta = reservedDelta;
        Type = type;
        OperationId = operationId;
        OccurredAt = occurredAt;
        Actor = actor;
    }

    public Guid Id { get; private set; }
    public Guid AtmId { get; private set; }
    public Guid CassetteId { get; private set; }
    public Currency Currency { get; private set; }
    public int Denomination { get; private set; }
    public int PhysicalDelta { get; private set; }
    public int ReservedDelta { get; private set; }
    public MovementType Type { get; private set; }
    public Guid OperationId { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
    public string Actor { get; private set; } = null!;
}
