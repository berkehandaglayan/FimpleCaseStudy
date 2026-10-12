using FimpleCaseStudy.Domain.Cash;

namespace FimpleCaseStudy.UnitTests;

public sealed class LowCashPolicyTests
{
    [Theory]
    [InlineData(CassetteKind.Recycling, 10, 9, false, true)]
    [InlineData(CassetteKind.Recycling, 100, 0, false, true)]
    [InlineData(CassetteKind.Recycling, 11, 10, false, false)]
    [InlineData(CassetteKind.Recycling, 9, 8, false, false)]
    [InlineData(CassetteKind.Recycling, 9, 9, false, false)]
    [InlineData(CassetteKind.Recycling, 9, 10, false, false)]
    [InlineData(CassetteKind.Recycling, 0, 0, true, true)]
    [InlineData(CassetteKind.Recycling, 9, 9, true, true)]
    [InlineData(CassetteKind.Recycling, 10, 10, true, false)]
    [InlineData(CassetteKind.Deposit, 10, 9, false, false)]
    [InlineData(CassetteKind.Deposit, 0, 0, true, false)]
    public void Only_recycling_crossing_or_first_service_below_ten_triggers(CassetteKind kind,
        int before, int after, bool firstService, bool expected) =>
        Assert.Equal(expected, LowCashPolicy.ShouldAlert(kind, before, after, firstService));
}
