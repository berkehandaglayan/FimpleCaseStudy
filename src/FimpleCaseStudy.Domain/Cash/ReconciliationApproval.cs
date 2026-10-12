namespace FimpleCaseStudy.Domain.Cash;

public enum ReconciliationStatus { LegacyCompleted, Completed, AwaitingApproval, Approved }
public sealed record ExchangeRateSnapshot(Currency Currency, decimal RateToTry);

public static class ReconciliationApproval
{
    public const decimal ThresholdTry = 1000m;

    public static string NormalizeActor(string? actor)
    {
        var value = actor?.Trim();
        if (string.IsNullOrEmpty(value) || value.Length > 128 || value.Any(c =>
                !(c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '.' or '_' or '-' or '@')))
            throw new DomainException("INVALID_ACTOR", "Use 1-128 ASCII letters, digits, dot, underscore, hyphen or @ for X-User.");
        return value.ToLowerInvariant();
    }

    public static decimal DifferenceTry(IReadOnlyList<ReconciliationLine> lines, IReadOnlyList<ExchangeRateSnapshot> rates)
    {
        if (rates.Count != 3 || rates.Any(r => r is null || !Enum.IsDefined(r.Currency) ||
                r.RateToTry <= 0 || r.RateToTry > 999999m || decimal.Round(r.RateToTry, 6) != r.RateToTry) ||
            rates.Select(r => r.Currency).Distinct().Count() != 3 || rates.Single(r => r.Currency == Currency.TRY).RateToTry != 1m)
            throw new DomainException("INVALID_EXCHANGE_RATES", "Provide TRY=1 and positive USD/EUR rates with at most six decimal places.", ErrorKind.Conflict);
        return lines.Sum(l => Math.Abs((decimal)l.Delta * l.Denomination * rates.Single(r => r.Currency == l.Currency).RateToTry));
    }

    public static ReconciliationStatus StatusFor(decimal differenceTry) =>
        differenceTry > ThresholdTry ? ReconciliationStatus.AwaitingApproval : ReconciliationStatus.Completed;

    // Returns false for an identical approval replay; no timestamp or cash mutation is repeated.
    public static bool ValidateApproval(ReconciliationStatus status, string enteredBy, string? approvedBy, string approver)
    {
        approver = NormalizeActor(approver);
        // Legacy operator names may not satisfy the new identity format; closed counts need no approval.
        if (status is ReconciliationStatus.LegacyCompleted or ReconciliationStatus.Completed)
            throw new DomainException("APPROVAL_NOT_REQUIRED", "This reconciliation does not require approval.", ErrorKind.Conflict);
        if (NormalizeActor(enteredBy) == approver)
            throw new DomainException("SAME_USER_APPROVAL", "A different operator must approve the reconciliation.", ErrorKind.Conflict);
        if (status == ReconciliationStatus.Approved)
        {
            if (approvedBy == approver) return false;
            throw new DomainException("RECONCILIATION_ALREADY_APPROVED", "This reconciliation has already been approved by another operator.", ErrorKind.Conflict);
        }
        if (status != ReconciliationStatus.AwaitingApproval)
            throw new DomainException("APPROVAL_NOT_REQUIRED", "This reconciliation does not require approval.", ErrorKind.Conflict);
        return true;
    }
}
