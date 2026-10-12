using FimpleCaseStudy.Domain;
using FimpleCaseStudy.Domain.Cash;

namespace FimpleCaseStudy.UnitTests;

public sealed class WithdrawalCashTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 12, 0, 0, TimeSpan.Zero);
    private static Atm Loaded()
    {
        var atm = Atm.Create("Withdrawal tests", Now);
        atm.Configure([new(1, Currency.TRY, 100), new(2, Currency.TRY, 50), new(3, Currency.TRY, 20), new(4, Currency.USD, 100)]);
        atm.Load([new(1, Currency.TRY, 100, 5), new(3, Currency.TRY, 20, 5), new(5, Currency.TRY, 100, 5)], "operator", Now);
        atm.TransitionTo(AtmState.InService);
        return atm;
    }
    private static DispenseLine Line(Atm atm, int slot, int denomination, int count) =>
        new(atm.Cassettes.Single(c => c.Slot == slot).Id, slot, denomination, count);

    [Fact]
    public void Duplicate_reservations_and_deposit_lines_cannot_modify_stock()
    {
        var atm = Loaded();
        var line = Line(atm, 1, 100, 1);
        Assert.Throws<DomainException>(() => atm.ChangeWithdrawalCash(Currency.TRY, [line, line], null, false, Guid.NewGuid(), Now));
        Assert.Throws<DomainException>(() => atm.ChangeWithdrawalCash(Currency.TRY, [Line(atm, 5, 100, 1)], null, false, Guid.NewGuid(), Now));
        Assert.All(atm.Cassettes, c => Assert.Equal(0, c.Reserved));
    }

    [Fact]
    public void Invalid_second_withdrawal_line_does_not_release_or_dispense_first_line()
    {
        var atm = Loaded();
        var lines = new[] { Line(atm, 1, 100, 1), Line(atm, 3, 20, 1) };
        atm.ChangeWithdrawalCash(Currency.TRY, lines, null, false, Guid.NewGuid(), Now);
        var invalid = new DispensePlan(140, [lines[0], lines[1] with { Count = 2 }]);
        Assert.Throws<DomainException>(() => atm.ChangeWithdrawalCash(Currency.TRY, lines, invalid, true, Guid.NewGuid(), Now));
        Assert.Equal(1, atm.Cassettes.Single(c => c.Slot == 1).Reserved);
        Assert.Equal(1, atm.Cassettes.Single(c => c.Slot == 3).Reserved);
        Assert.Equal(5, atm.Cassettes.Single(c => c.Slot == 1).OnHand);
        Assert.Equal(5, atm.Cassettes.Single(c => c.Slot == 3).OnHand);
    }

    [Fact]
    public void Reserve_and_withdraw_require_service_but_release_is_allowed_in_maintenance()
    {
        var atm = Loaded();
        var line = Line(atm, 1, 100, 1);
        atm.ChangeWithdrawalCash(Currency.TRY, [line], null, false, Guid.NewGuid(), Now);
        atm.TransitionTo(AtmState.Maintenance);
        Assert.Throws<DomainException>(() => atm.ChangeWithdrawalCash(Currency.TRY, [line], null, false, Guid.NewGuid(), Now));
        Assert.Throws<DomainException>(() => atm.ChangeWithdrawalCash(Currency.TRY, [line], new(100, [line]), true, Guid.NewGuid(), Now));
        var movement = Assert.Single(atm.ChangeWithdrawalCash(Currency.TRY, [line], null, true, Guid.NewGuid(), Now));
        Assert.Equal(-1, movement.ReservedDelta);
        Assert.Equal(0, movement.PhysicalDelta);
    }
}
