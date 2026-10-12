using FimpleCaseStudy.Domain;
using FimpleCaseStudy.Domain.Cash;

namespace FimpleCaseStudy.UnitTests;

public sealed class ReconciliationApprovalTests
{
    private static ExchangeRateSnapshot[] Rates() => [new(Currency.TRY, 1m), new(Currency.USD, 40m), new(Currency.EUR, 45m)];
    private static ReconciliationLine Line(Currency currency, int denomination, int delta) =>
        new(Guid.NewGuid(), 1, currency, denomination, 0, Math.Abs(delta), delta);

    [Theory]
    [InlineData(0, ReconciliationStatus.Completed)]
    [InlineData(1000, ReconciliationStatus.Completed)]
    [InlineData(1001, ReconciliationStatus.AwaitingApproval)]
    public void Threshold_is_strictly_greater_than_one_thousand(int difference, ReconciliationStatus status) =>
        Assert.Equal(status, ReconciliationApproval.StatusFor(difference));

    [Fact]
    public void Mixed_signed_differences_use_absolute_value_before_sum_and_exact_decimal_fx()
    {
        var lines = new[] { Line(Currency.TRY, 500, -1), Line(Currency.TRY, 500, 1),
            Line(Currency.USD, 20, -1), Line(Currency.EUR, 50, 1) };
        Assert.Equal(4050m, ReconciliationApproval.DifferenceTry(lines, Rates()));
        Assert.Equal(1000m, ReconciliationApproval.DifferenceTry(lines[..2], Rates()));
        var fractional = new[] { new ExchangeRateSnapshot(Currency.TRY, 1m), new(Currency.USD, 0.000001m), new(Currency.EUR, 45m) };
        Assert.Equal(0.00002m, ReconciliationApproval.DifferenceTry([Line(Currency.USD, 20, 1)], fractional));
        fractional[1] = new(Currency.USD, 50.000001m);
        var justAbove = ReconciliationApproval.DifferenceTry([Line(Currency.USD, 20, 1)], fractional);
        Assert.Equal(1000.00002m, justAbove);
        Assert.Equal(ReconciliationStatus.AwaitingApproval, ReconciliationApproval.StatusFor(justAbove));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("duplicate")]
    [InlineData("zero")]
    [InlineData("negative")]
    [InlineData("too-large")]
    [InlineData("precision")]
    [InlineData("try-not-one")]
    [InlineData("unknown")]
    public void Invalid_rate_table_is_rejected(string invalid)
    {
        var rates = Rates();
        switch (invalid)
        {
            case "missing": rates = rates[..2]; break;
            case "duplicate": rates[2] = rates[1]; break;
            case "zero": rates[1] = new(Currency.USD, 0); break;
            case "negative": rates[1] = new(Currency.USD, -1); break;
            case "too-large": rates[1] = new(Currency.USD, 1000000); break;
            case "precision": rates[1] = new(Currency.USD, 0.0000001m); break;
            case "try-not-one": rates[0] = new(Currency.TRY, 2); break;
            case "unknown": rates[1] = new((Currency)99, 40); break;
        }
        Assert.Equal("INVALID_EXCHANGE_RATES", Assert.Throws<DomainException>(() =>
            ReconciliationApproval.DifferenceTry([Line(Currency.TRY, 100, 1)], rates)).Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("alice,bob")]
    [InlineData("ali ce")]
    [InlineData("alıce")]
    public void Invalid_actor_is_not_an_identity(string actor) =>
        Assert.Equal("INVALID_ACTOR", Assert.Throws<DomainException>(() => ReconciliationApproval.NormalizeActor(actor)).Code);

    [Fact]
    public void Canonical_identity_prevents_case_and_whitespace_self_approval_and_replay_does_not_mutate()
    {
        Assert.Equal("alice", ReconciliationApproval.NormalizeActor(" ALICE "));
        Assert.Equal("SAME_USER_APPROVAL", Assert.Throws<DomainException>(() =>
            ReconciliationApproval.ValidateApproval(ReconciliationStatus.AwaitingApproval, " Alice ", null, "alice")).Code);
        Assert.True(ReconciliationApproval.ValidateApproval(ReconciliationStatus.AwaitingApproval, "alice", null, "bob"));
        Assert.False(ReconciliationApproval.ValidateApproval(ReconciliationStatus.Approved, "alice", "bob", "bob"));
        Assert.Equal("RECONCILIATION_ALREADY_APPROVED", Assert.Throws<DomainException>(() =>
            ReconciliationApproval.ValidateApproval(ReconciliationStatus.Approved, "alice", "bob", "carol")).Code);
        Assert.Equal("APPROVAL_NOT_REQUIRED", Assert.Throws<DomainException>(() =>
            ReconciliationApproval.ValidateApproval(ReconciliationStatus.Completed, "alice", null, "bob")).Code);
        Assert.Equal("APPROVAL_NOT_REQUIRED", Assert.Throws<DomainException>(() =>
            ReconciliationApproval.ValidateApproval(ReconciliationStatus.LegacyCompleted, "Old User", null, "bob")).Code);
    }
}
