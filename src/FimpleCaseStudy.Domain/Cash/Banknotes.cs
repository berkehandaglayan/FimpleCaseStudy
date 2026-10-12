namespace FimpleCaseStudy.Domain.Cash;

public enum Currency { TRY, USD, EUR }
public enum CassetteKind { Recycling, Deposit }
public enum AtmState { Unconfigured, Configured, Loaded, InService, Maintenance }
public enum MovementType { Load, Unload, Withdrawal, Deposit, Reservation, ReservationRelease, ReconciliationAdjustment }

public sealed record CassetteConfiguration(int Slot, Currency Currency, int Denomination);
public sealed record CashLine(int Slot, Currency Currency, int Denomination, int Count);

public static class Banknotes
{
    public static void Validate(Currency currency, int denomination)
    {
        if (!Enum.IsDefined(currency))
            throw new DomainException("UNSUPPORTED_CURRENCY", "Currency must be TRY, USD or EUR.");
        if (denomination is not (20 or 50 or 100 or 500))
            throw new DomainException("UNSUPPORTED_DENOMINATION", "Denomination must be 20, 50, 100 or 500.");
    }
}
