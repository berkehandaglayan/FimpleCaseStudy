using FimpleCaseStudy.Domain;
using FimpleCaseStudy.Domain.Cash;

namespace FimpleCaseStudy.UnitTests;

public sealed class ReconciliationTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 12, 0, 0, TimeSpan.Zero);
    private static Atm Ready()
    {
        var atm = Atm.Create("Count ATM", Now);
        atm.Configure([new(1, Currency.TRY, 100), new(2, Currency.TRY, 50), new(3, Currency.TRY, 20), new(4, Currency.USD, 100)]);
        atm.Load([new(1, Currency.TRY, 100, 10), new(5, Currency.EUR, 50, 2)], "operator", Now);
        atm.TransitionTo(AtmState.InService);
        atm.TransitionTo(AtmState.Maintenance);
        return atm;
    }
    private static List<CassetteCount> Counts() =>
    [
        new(1, [new(Currency.TRY, 100, 9)]), new(2, [new(Currency.TRY, 50, 1)]),
        new(3, [new(Currency.TRY, 20, 0)]), new(4, [new(Currency.USD, 100, 0)]),
        new(5, [new(Currency.USD, 20, 3)])
    ];

    [Fact]
    public void Full_count_records_signed_deltas_new_notes_and_missing_deposit_notes_as_zero()
    {
        var atm = Ready();
        var result = atm.Reconcile(Counts(), "alice", Now.ToOffset(TimeSpan.FromHours(3)));
        Assert.Equal(new[] { -1, 1, 3, -2 }, result.Movements.Select(m => m.PhysicalDelta));
        Assert.All(result.Movements, m =>
        {
            Assert.Equal(MovementType.ReconciliationAdjustment, m.Type);
            Assert.Equal(0, m.ReservedDelta); Assert.Equal("alice", m.Actor);
            Assert.Equal(result.Receipt.OperationId, m.OperationId); Assert.Equal(TimeSpan.Zero, m.OccurredAt.Offset);
        });
        var omitted = Assert.Single(result.Receipt.Lines, l => l.Slot == 5 && l.Currency == Currency.EUR);
        Assert.Equal((2, 0, -2), (omitted.StoredCount, omitted.CountedCount, omitted.Delta));
        Assert.Equal(new[] { 9, 1, 0, 0, 3 }, atm.Cassettes.Select(c => c.OnHand));
        Assert.Equal(AtmState.Maintenance, atm.State);
    }

    [Fact]
    public void Same_count_has_receipt_and_zero_delta_lines_but_no_new_movements()
    {
        var atm = Ready();
        var counts = Counts();
        atm.Reconcile(counts, "alice", Now);
        var repeated = atm.Reconcile(counts, "bob", Now);
        Assert.Empty(repeated.Movements); Assert.NotEqual(Guid.Empty, repeated.Receipt.OperationId);
        Assert.All(repeated.Receipt.Lines, l => Assert.Equal(0, l.Delta));
        Assert.Contains(repeated.Receipt.Lines, l => l.Slot == 3 && l.CountedCount == 0);
    }

    [Theory]
    [InlineData("missing-slot")]
    [InlineData("duplicate-slot")]
    [InlineData("null-notes")]
    [InlineData("duplicate-note")]
    [InlineData("negative")]
    [InlineData("over-100")]
    [InlineData("mixed-capacity")]
    [InlineData("wrong-currency")]
    [InlineData("invalid-currency")]
    [InlineData("invalid-denomination")]
    [InlineData("empty-recycling")]
    [InlineData("null-note")]
    public void Invalid_later_count_does_not_change_earlier_valid_cassette(string invalid)
    {
        var atm = Ready();
        var counts = Counts();
        switch (invalid)
        {
            case "missing-slot": counts.RemoveAt(4); break;
            case "duplicate-slot": counts[4] = counts[3]; break;
            case "null-notes": counts[4] = new(5, null); break;
            case "duplicate-note": counts[4] = new(5, [new(Currency.EUR, 50, 1), new(Currency.EUR, 50, 1)]); break;
            case "negative": counts[4] = new(5, [new(Currency.EUR, 50, -1)]); break;
            case "over-100": counts[4] = new(5, [new(Currency.EUR, 50, 101)]); break;
            case "mixed-capacity": counts[4] = new(5, [new(Currency.EUR, 50, 60), new(Currency.USD, 20, 41)]); break;
            case "wrong-currency": counts[3] = new(4, [new(Currency.EUR, 100, 1)]); break;
            case "invalid-currency": counts[4] = new(5, [new((Currency)99, 50, 1)]); break;
            case "invalid-denomination": counts[4] = new(5, [new(Currency.EUR, 10, 1)]); break;
            case "empty-recycling": counts[3] = new(4, []); break;
            case "null-note": counts[4] = new(5, [null!]); break;
        }
        Assert.Throws<DomainException>(() => atm.Reconcile(counts, "alice", Now));
        Assert.Equal(10, atm.Cassettes.Single(c => c.Slot == 1).OnHand);
        Assert.Equal(2, atm.Cassettes.Single(c => c.Slot == 5).OnHand);
    }

    [Fact]
    public void Maintenance_and_zero_reservations_are_required()
    {
        var atm = Ready();
        atm.TransitionTo(AtmState.InService);
        Assert.Equal("INVALID_ATM_STATE", Assert.Throws<DomainException>(() => atm.Reconcile(Counts(), "alice", Now)).Code);
        var plan = BanknoteSelector.Select(atm, Currency.TRY, 100, 20000).Exact!;
        atm.ChangeWithdrawalCash(Currency.TRY, plan.Lines, null, false, Guid.NewGuid(), Now);
        atm.TransitionTo(AtmState.Maintenance);
        Assert.Equal("ACTIVE_RESERVATIONS", Assert.Throws<DomainException>(() => atm.Reconcile(Counts(), "alice", Now)).Code);
    }

    [Fact]
    public void Empty_deposit_clears_every_currency_and_invalid_actor_is_rejected()
    {
        var atm = Ready();
        var counts = Counts(); counts[4] = new(5, []);
        foreach (var actor in new[] { "", " ", new string('x', 129) })
            Assert.Equal("INVALID_ACTOR", Assert.Throws<DomainException>(() => atm.Reconcile(counts, actor, Now)).Code);
        atm.Reconcile(counts, "alice", Now);
        Assert.Equal(0, atm.Cassettes.Single(c => c.Slot == 5).OnHand);
    }

    [Fact]
    public void Reconfigured_empty_cassette_keeps_historical_zero_line_without_adjustment()
    {
        var atm = Ready();
        atm.Unload([new(1, Currency.TRY, 100, 10)], "alice", Now);
        atm.Configure([new(1, Currency.TRY, 500), new(2, Currency.TRY, 50), new(3, Currency.TRY, 20), new(4, Currency.USD, 100)]);
        var counts = Counts(); counts[0] = new(1, [new(Currency.TRY, 500, 1)]);
        var result = atm.Reconcile(counts, "alice", Now);
        Assert.Contains(result.Receipt.Lines, l => l.Slot == 1 && l.Denomination == 100 && l.Delta == 0);
        var cassetteId = atm.Cassettes.Single(c => c.Slot == 1).Id;
        Assert.DoesNotContain(result.Movements, m => m.CassetteId == cassetteId && m.Denomination == 100);
        Assert.Equal(1, atm.Cassettes.Single(c => c.Slot == 1).OnHand);
    }
}
