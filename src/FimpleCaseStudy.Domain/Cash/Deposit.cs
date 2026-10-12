namespace FimpleCaseStudy.Domain.Cash;

// Recognition is supplied by the note reader. Unknown notes carry no invented value.
public sealed record DepositedNote(string? Currency, int? Denomination, int Count, bool Recognized = true);
public sealed record DepositPlacement(int InputIndex, Guid CassetteId, int Slot, Currency Currency,
    int Denomination, int Count);
public sealed record ReturnedNote(int InputIndex, string? Currency, int? Denomination, int Count, string Reason);
public sealed record DepositReceipt(Guid OperationId, IReadOnlyList<DepositPlacement> Placements,
    IReadOnlyList<ReturnedNote> Returned)
{
    public int AcceptedCount => Placements.Sum(p => p.Count);
    public int ReturnedCount => Returned.Sum(r => r.Count);
}
public sealed record DepositChange(DepositReceipt Receipt, IReadOnlyList<CashMovement> Movements);
