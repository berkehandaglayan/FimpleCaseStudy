using FimpleCaseStudy.Domain.Cash;

namespace FimpleCaseStudy.Application.Withdrawals;

public sealed record QuoteRequest(string CardNumber, Currency Currency, int Amount);
public sealed record ConfirmRequest(string Option);
public sealed record QuoteOption(string Name, DispensePlan Plan);
public sealed record QuoteView(Guid Id, Guid AtmId, Currency Currency, string Status,
    DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt, IReadOnlyList<QuoteOption> Options, string? SelectedOption);
public sealed record ConfirmationResult(int StatusCode, string Json);

public interface IWithdrawalService
{
    Task<QuoteView> QuoteAsync(Guid atmId, QuoteRequest request, CancellationToken ct);
    Task<QuoteView> GetAsync(Guid atmId, Guid quoteId, CancellationToken ct);
    Task<ConfirmationResult> ConfirmAsync(Guid atmId, Guid quoteId, ConfirmRequest request, string key, CancellationToken ct);
    Task<QuoteView> CancelAsync(Guid atmId, Guid quoteId, CancellationToken ct);
    Task ExpireAsync(CancellationToken ct);
}
