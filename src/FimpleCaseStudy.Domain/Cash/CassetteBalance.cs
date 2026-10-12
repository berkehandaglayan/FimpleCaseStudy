namespace FimpleCaseStudy.Domain.Cash;

public sealed class CassetteBalance
{
    private CassetteBalance() { }
    internal CassetteBalance(Guid cassetteId, Currency currency, int denomination)
    {
        CassetteId = cassetteId;
        Currency = currency;
        Denomination = denomination;
    }

    public Guid CassetteId { get; private set; }
    public Currency Currency { get; private set; }
    public int Denomination { get; private set; }
    public int OnHand { get; private set; }
    public int Reserved { get; private set; }
    public int Available => OnHand - Reserved;

    internal void ChangePhysicalCount(int delta) => OnHand = checked(OnHand + delta);
    internal void ChangeReservation(int delta) => Reserved = checked(Reserved + delta);
}
