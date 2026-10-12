using FimpleCaseStudy.Domain;
using FimpleCaseStudy.Domain.Cash;

namespace FimpleCaseStudy.UnitTests;

public sealed class DepositTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 12, 0, 0, TimeSpan.Zero);
    private static Atm Ready(int first = 90, int second = 95, int deposit = 0)
    {
        var atm = Atm.Create("Deposit ATM", Now);
        atm.Configure([new(1, Currency.TRY, 100), new(2, Currency.TRY, 100), new(3, Currency.TRY, 20), new(4, Currency.USD, 100)]);
        var lines = new List<CashLine> { new(1, Currency.TRY, 100, first), new(2, Currency.TRY, 100, second) };
        if (deposit > 0) lines.Add(new(5, Currency.EUR, 50, deposit));
        atm.Load(lines, "operator", Now);
        atm.TransitionTo(AtmState.InService);
        return atm;
    }

    [Fact]
    public void Matching_recycling_then_overflow_to_deposit_then_return()
    {
        var atm = Ready(deposit: 98);
        var change = atm.Deposit([new("TRY", 100, 20)], Now);
        Assert.Equal(new[] { (1, 10), (2, 5), (5, 2) }, change.Receipt.Placements.Select(p => (p.Slot, p.Count)));
        var returned = Assert.Single(change.Receipt.Returned);
        Assert.Equal((3, "CAPACITY_EXCEEDED"), (returned.Count, returned.Reason));
        Assert.Equal(17, change.Receipt.AcceptedCount);
        Assert.All(atm.Cassettes.Where(c => c.Slot is 1 or 2 or 5), c => Assert.Equal(100, c.OnHand));
        Assert.All(change.Movements, m =>
        {
            Assert.Equal(MovementType.Deposit, m.Type);
            Assert.Equal(0, m.ReservedDelta);
            Assert.Equal(change.Receipt.OperationId, m.OperationId);
        });
    }

    [Fact]
    public void Duplicate_input_groups_share_capacity_and_merge_ledger_lines()
    {
        var atm = Ready(99, 100, 99);
        var change = atm.Deposit([new("TRY", 100, 1), new("TRY", 100, 3)], Now);
        Assert.Equal(2, change.Receipt.AcceptedCount);
        Assert.Equal(2, change.Receipt.ReturnedCount);
        Assert.Equal(1, Assert.Single(change.Receipt.Returned).InputIndex);
        Assert.Equal(2, change.Movements.Count);
        Assert.All(atm.Cassettes, c => Assert.InRange(c.OnHand, 0, 100));
    }

    [Fact]
    public void Same_fullness_uses_slot_order_and_nonmatching_notes_use_mixed_deposit()
    {
        var atm = Ready(95, 95);
        var change = atm.Deposit([new("try", 100, 1), new("EUR", 500, 2), new("USD", 20, 3)], Now);
        Assert.Equal(new[] { 1, 5, 5 }, change.Receipt.Placements.Select(p => p.Slot));
        Assert.Equal(5, atm.Cassettes.Single(c => c.Slot == 5).OnHand);
        Assert.Empty(change.Receipt.Returned);
    }

    [Fact]
    public void Reservations_do_not_create_capacity_and_remain_unchanged()
    {
        var atm = Ready(100, 100);
        var selection = BanknoteSelector.Select(atm, Currency.TRY, 1000, 20000);
        atm.ChangeWithdrawalCash(Currency.TRY, selection.Exact!.Lines, null, false, Guid.NewGuid(), Now);
        var change = atm.Deposit([new("TRY", 100, 1)], Now);
        Assert.Equal(5, Assert.Single(change.Receipt.Placements).Slot);
        Assert.Equal(10, atm.Cassettes.Sum(c => c.Reserved));
        Assert.Equal(100, atm.Cassettes.Single(c => c.Slot == 1).OnHand);
    }

    [Fact]
    public void Unknown_notes_never_get_value_or_ledger_and_do_not_consume_capacity()
    {
        var atm = Ready();
        var before = atm.Cassettes.Sum(c => c.OnHand);
        var change = atm.Deposit([new(null, null, 2, false), new("GBP", 100, 1), new("TRY", 10, 3),
            new("USD", 100, 1, false), new("0", 20, 1)], Now);
        Assert.Empty(change.Movements); Assert.Empty(change.Receipt.Placements);
        Assert.Equal(8, change.Receipt.ReturnedCount);
        Assert.All(change.Receipt.Returned, r => Assert.Equal("UNRECOGNIZED", r.Reason));
        Assert.Equal(before, atm.Cassettes.Sum(c => c.OnHand));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(10001)]
    [InlineData(int.MaxValue)]
    public void Invalid_second_line_leaves_every_balance_unchanged(int count)
    {
        var atm = Ready();
        Assert.Equal("INVALID_DEPOSIT_NOTES", Assert.Throws<DomainException>(() =>
            atm.Deposit([new("TRY", 100, 1), new("EUR", 50, count)], Now)).Code);
        Assert.Equal(90, atm.Cassettes.Single(c => c.Slot == 1).OnHand);
        Assert.Equal(0, atm.Cassettes.Single(c => c.Slot == 5).OnHand);
    }

    [Fact]
    public void Total_input_bound_and_missing_groups_are_checked_before_mutation()
    {
        var atm = Ready();
        foreach (var notes in new IReadOnlyList<DepositedNote>?[] { null, [], [new("TRY", 20, 6000), new("USD", 20, 6000)],
                     [null!], [new(new string('X', 33), 20, 1)], Enumerable.Repeat(new DepositedNote("TRY", 20, 1), 101).ToArray() })
            Assert.Throws<DomainException>(() => atm.Deposit(notes, Now));
        Assert.Equal(185, atm.Cassettes.Sum(c => c.OnHand));
    }

    [Fact]
    public void Maintenance_rejects_customer_deposits()
    {
        var atm = Ready();
        atm.TransitionTo(AtmState.Maintenance);
        Assert.Equal("INVALID_ATM_STATE", Assert.Throws<DomainException>(() => atm.Deposit([new("TRY", 20, 1)], Now)).Code);
    }

    [Fact]
    public void Accepted_plus_returned_equals_every_input_group_in_mixed_batch()
    {
        var atm = Ready(99, 99, 98);
        var notes = new[] { new DepositedNote("TRY", 100, 5), new("EUR", 50, 4), new("USD", 100, 150), new(null, null, 5, false) };
        var change = atm.Deposit(notes, Now);
        for (var index = 0; index < notes.Length; index++)
            Assert.Equal(notes[index].Count, change.Receipt.Placements.Where(p => p.InputIndex == index).Sum(p => p.Count) +
                change.Receipt.Returned.Where(r => r.InputIndex == index).Sum(r => r.Count));
        Assert.All(atm.Cassettes, c => Assert.InRange(c.OnHand, 0, 100));
    }
}
