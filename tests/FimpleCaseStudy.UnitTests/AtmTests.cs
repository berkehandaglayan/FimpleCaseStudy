using FimpleCaseStudy.Domain;
using FimpleCaseStudy.Domain.Cash;

namespace FimpleCaseStudy.UnitTests;

public sealed class AtmTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 10, 0, 0, TimeSpan.Zero);

    private static CassetteConfiguration[] Configuration() =>
    [new(1, Currency.TRY, 100), new(2, Currency.TRY, 50), new(3, Currency.TRY, 20), new(4, Currency.USD, 100)];

    private static Atm Configured()
    {
        var atm = Atm.Create("ATM", Now);
        atm.Configure(Configuration());
        return atm;
    }

    [Fact]
    public void Creation_provides_five_fixed_slots_and_only_one_deposit_cassette()
    {
        var atm = Atm.Create(" Branch ATM ", Now);
        Assert.Equal("Branch ATM", atm.Name);
        Assert.Equal(AtmState.Unconfigured, atm.State);
        Assert.Equal(5, atm.Cassettes.Count);
        Assert.Equal(5, Assert.Single(atm.Cassettes, c => c.Kind == CassetteKind.Deposit).Slot);
        Assert.Equal(4, atm.Cassettes.Count(c => c.Kind == CassetteKind.Recycling));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Empty_name_is_rejected(string? name) =>
        Assert.Equal("INVALID_ATM_NAME", Assert.Throws<DomainException>(() => Atm.Create(name, Now)).Code);

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(4)]
    public void Foreign_cassette_count_outside_one_to_two_is_rejected(int foreignCount)
    {
        var config = Enumerable.Range(1, 4).Select(slot =>
            new CassetteConfiguration(slot, slot <= foreignCount ? Currency.USD : Currency.TRY, 100)).ToArray();
        var atm = Atm.Create("ATM", Now);
        Assert.Equal("INVALID_FOREIGN_CASSETTE_COUNT", Assert.Throws<DomainException>(() => atm.Configure(config)).Code);
        Assert.Equal(AtmState.Unconfigured, atm.State);
        Assert.All(atm.Cassettes, c => Assert.Null(c.Currency));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void One_or_two_foreign_cassettes_and_duplicate_denominations_are_allowed(int foreignCount)
    {
        var atm = Atm.Create("ATM", Now);
        atm.Configure(Enumerable.Range(1, 4).Select(slot =>
            new CassetteConfiguration(slot, slot <= foreignCount ? Currency.EUR : Currency.TRY, 100)).ToArray());
        Assert.Equal(AtmState.Configured, atm.State);
    }

    [Fact]
    public void Duplicate_or_missing_slots_are_rejected()
    {
        var config = Configuration();
        config[3] = config[3] with { Slot = 1 };
        Assert.Equal("INVALID_CASSETTE_CONFIGURATION",
            Assert.Throws<DomainException>(() => Atm.Create("ATM", Now).Configure(config)).Code);
    }

    [Theory]
    [InlineData(10)]
    [InlineData(200)]
    [InlineData(-20)]
    public void Unsupported_denominations_are_rejected(int denomination)
    {
        var config = Configuration();
        config[0] = config[0] with { Denomination = denomination };
        Assert.Equal("UNSUPPORTED_DENOMINATION",
            Assert.Throws<DomainException>(() => Atm.Create("ATM", Now).Configure(config)).Code);
    }

    [Fact]
    public void Unknown_currency_is_rejected()
    {
        var config = Configuration();
        config[0] = config[0] with { Currency = (Currency)99 };
        Assert.Equal("UNSUPPORTED_CURRENCY",
            Assert.Throws<DomainException>(() => Atm.Create("ATM", Now).Configure(config)).Code);
    }

    [Fact]
    public void Mixed_deposit_capacity_is_combined_and_invalid_batch_has_no_partial_effect()
    {
        var atm = Configured();
        Assert.Equal("CASSETTE_CAPACITY_EXCEEDED", Assert.Throws<DomainException>(() => atm.Load(
            [new(1, Currency.TRY, 100, 10), new(5, Currency.TRY, 20, 60), new(5, Currency.EUR, 50, 41)], "operator", Now)).Code);
        Assert.Equal(AtmState.Configured, atm.State);
        Assert.All(atm.Cassettes, c => Assert.Equal(0, c.OnHand));
    }

    [Fact]
    public void Load_records_every_line_and_total_deposit_capacity_of_100_is_allowed()
    {
        var atm = Configured();
        var movements = atm.Load(
            [new(1, Currency.TRY, 100, 10), new(5, Currency.TRY, 20, 60), new(5, Currency.EUR, 50, 40)], "operator", Now);
        Assert.Equal(AtmState.Loaded, atm.State);
        Assert.Equal(100, atm.Cassettes.Single(c => c.Slot == 5).OnHand);
        Assert.Equal(3, movements.Count);
        Assert.Single(movements.Select(m => m.OperationId).Distinct());
        Assert.All(movements, m => { Assert.Equal(MovementType.Load, m.Type); Assert.Equal(0, m.ReservedDelta); });
    }

    [Fact]
    public void Invalid_second_line_does_not_apply_first_line()
    {
        var atm = Configured();
        Assert.Equal("CASSETTE_NOTE_MISMATCH", Assert.Throws<DomainException>(() =>
            atm.Load([new(1, Currency.TRY, 100, 10), new(2, Currency.USD, 50, 1)], "operator", Now)).Code);
        Assert.All(atm.Cassettes, c => Assert.Equal(0, c.OnHand));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(101)]
    [InlineData(int.MaxValue)]
    public void Invalid_counts_are_rejected_before_arithmetic(int count)
    {
        Assert.Equal("INVALID_CASH_LINE", Assert.Throws<DomainException>(() =>
            Configured().Load([new(1, Currency.TRY, 100, count)], "operator", Now)).Code);
    }

    [Fact]
    public void Initial_load_requires_recycling_cash_and_cannot_be_skipped()
    {
        var atm = Configured();
        Assert.Equal("INITIAL_RECYCLING_CASH_REQUIRED", Assert.Throws<DomainException>(() =>
            atm.Load([new(5, Currency.USD, 100, 1)], "operator", Now)).Code);
        Assert.Throws<DomainException>(() => atm.TransitionTo(AtmState.Loaded));
        Assert.Throws<DomainException>(() => atm.TransitionTo(AtmState.InService));
    }

    [Fact]
    public void Lifecycle_requires_maintenance_for_more_loads_and_reconfiguration_requires_empty_cash()
    {
        var atm = Configured();
        atm.Load([new(1, Currency.TRY, 100, 10)], "operator", Now);
        Assert.Throws<DomainException>(() => atm.Load([new(1, Currency.TRY, 100, 1)], "operator", Now));
        atm.TransitionTo(AtmState.InService);
        Assert.Throws<DomainException>(() => atm.Configure(Configuration()));
        Assert.Throws<DomainException>(() => atm.Unload([new(1, Currency.TRY, 100, 1)], "operator", Now));
        atm.TransitionTo(AtmState.Maintenance);
        var changed = Configuration();
        changed[0] = changed[0] with { Denomination = 500 };
        Assert.Equal("CASSETTE_NOT_EMPTY", Assert.Throws<DomainException>(() => atm.Configure(changed)).Code);
        var movements = atm.Unload([new(1, Currency.TRY, 100, 10)], "operator", Now);
        Assert.Equal(-10, Assert.Single(movements).PhysicalDelta);
        atm.Configure(changed);
        Assert.Equal(500, atm.Cassettes.Single(c => c.Slot == 1).Denomination);
        Assert.Equal(AtmState.Maintenance, atm.State);
        atm.TransitionTo(AtmState.InService);
    }

    [Fact]
    public void Over_unload_and_duplicate_lines_are_rejected()
    {
        var atm = Configured();
        Assert.Equal("DUPLICATE_CASH_LINE", Assert.Throws<DomainException>(() =>
            atm.Load([new(1, Currency.TRY, 100, 10), new(1, Currency.TRY, 100, 10)], "operator", Now)).Code);
        atm.Load([new(1, Currency.TRY, 100, 10)], "operator", Now);
        atm.TransitionTo(AtmState.InService);
        atm.TransitionTo(AtmState.Maintenance);
        Assert.Equal("INSUFFICIENT_CASH", Assert.Throws<DomainException>(() =>
            atm.Unload([new(1, Currency.TRY, 100, 11)], "operator", Now)).Code);
        Assert.Equal(10, atm.Cassettes.Single(c => c.Slot == 1).OnHand);
    }
}
