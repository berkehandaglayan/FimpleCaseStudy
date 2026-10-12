using FimpleCaseStudy.Domain;
using FimpleCaseStudy.Domain.Cash;

namespace FimpleCaseStudy.UnitTests;

public sealed class BanknoteSelectorTests
{
    private static RecyclingStock[] Stock(Currency currency, params (int Denomination, int Count)[] notes) =>
        notes.Select((n, i) => new RecyclingStock(Guid.NewGuid(), i + 1, currency, n.Denomination, n.Count, 0)).ToArray();

    private static void Plan(DispensePlan? plan, int amount, params (int Denomination, int Count)[] expected)
    {
        Assert.NotNull(plan);
        Assert.Equal(amount, plan.Amount);
        Assert.Equal(expected.Sum(x => x.Count), plan.NoteCount);
        Assert.Equal(expected.OrderByDescending(x => x.Denomination),
            plan.Lines.GroupBy(x => x.Denomination).Select(g => (g.Key, g.Sum(x => x.Count))).OrderByDescending(x => x.Key));
    }

    [Fact]
    public void B3_01_greedy_trap_160()
    {
        var result = BanknoteSelector.Select(Stock(Currency.TRY, (100, 0), (50, 3), (20, 10)), Currency.TRY, 160, 20000);
        Plan(result.Exact, 160, (50, 2), (20, 3));
        Assert.Null(result.Lower); Assert.Null(result.Upper);
    }

    [Fact]
    public void B3_02_bounded_stock_offers_70_and_90()
    {
        var result = BanknoteSelector.Select(Stock(Currency.TRY, (50, 1), (20, 2)), Currency.TRY, 80, 20000);
        Assert.Null(result.Exact);
        Plan(result.Lower, 70, (50, 1), (20, 1)); Plan(result.Upper, 90, (50, 1), (20, 2));
    }

    [Fact]
    public void B3_03_offers_120_and_140()
    {
        var result = BanknoteSelector.Select(Stock(Currency.TRY, (100, 5), (20, 10)), Currency.TRY, 130, 20000);
        Assert.Null(result.Exact);
        Plan(result.Lower, 120, (100, 1), (20, 1)); Plan(result.Upper, 140, (100, 1), (20, 2));
    }

    [Fact]
    public void B3_04_usd_stock_has_only_lower_200()
    {
        var result = BanknoteSelector.Select(Stock(Currency.USD, (100, 2)), Currency.USD, 250, 1000);
        Plan(result.Lower, 200, (100, 2)); Assert.Null(result.Exact); Assert.Null(result.Upper);
    }

    [Fact]
    public void B3_05_600_uses_available_twenty_notes()
    {
        var result = BanknoteSelector.Select(Stock(Currency.TRY, (500, 1), (100, 0), (20, 5)), Currency.TRY, 600, 20000);
        Plan(result.Exact, 600, (500, 1), (20, 5));
    }

    [Fact]
    public void B3_06_200_minimizes_note_count()
    {
        var result = BanknoteSelector.Select(Stock(Currency.TRY, (100, 1), (50, 4), (20, 5)), Currency.TRY, 200, 20000);
        Plan(result.Exact, 200, (100, 1), (50, 2));
    }

    [Fact]
    public void B3_07_1000_respects_all_stock_bounds()
    {
        var result = BanknoteSelector.Select(Stock(Currency.TRY, (500, 1), (100, 3), (20, 20)), Currency.TRY, 1000, 20000);
        Plan(result.Exact, 1000, (500, 1), (100, 3), (20, 10));
    }

    [Fact]
    public void B3_08_110_offers_100_and_120()
    {
        var result = BanknoteSelector.Select(Stock(Currency.TRY, (100, 3), (50, 1), (20, 1)), Currency.TRY, 110, 20000);
        Assert.Null(result.Exact);
        Plan(result.Lower, 100, (100, 1)); Plan(result.Upper, 120, (100, 1), (20, 1));
    }

    [Theory]
    [InlineData(119, false)]
    [InlineData(120, true)]
    public void Upper_option_must_fit_remaining_limit(int limit, bool hasUpper)
    {
        var result = BanknoteSelector.Select(Stock(Currency.TRY, (100, 3), (20, 1)), Currency.TRY, 110, limit);
        Plan(result.Lower, 100, (100, 1)); Assert.Equal(hasUpper, result.Upper is not null);
    }

    [Fact]
    public void Fullest_physical_cassette_wins_but_reserved_notes_are_not_spent()
    {
        var stock = Stock(Currency.TRY, (100, 40), (100, 90), (100, 90));
        stock[1] = stock[1] with { Reserved = 88 };
        var plan = BanknoteSelector.Select(stock.Reverse().ToArray(), Currency.TRY, 500, 20000).Exact!;
        Assert.Equal(new[] { (2, 2), (3, 3) }, plan.Lines.Select(x => (x.Slot, x.Count)));
        Assert.Equal(88, stock[1].Reserved);
        Assert.Equal(90, stock[1].OnHand);
    }

    [Fact]
    public void Other_currencies_and_fully_reserved_stock_are_excluded()
    {
        var stock = Stock(Currency.TRY, (100, 2), (50, 2));
        stock[0] = stock[0] with { Currency = Currency.USD };
        stock[1] = stock[1] with { Reserved = 2 };
        Assert.Equal(new BanknoteSelection(null, null, null), BanknoteSelector.Select(stock, Currency.TRY, 100, 20000));
    }

    [Fact]
    public void Zero_is_not_an_option_and_non_multiple_of_ten_is_not_rounded()
    {
        var result = BanknoteSelector.Select(Stock(Currency.EUR, (20, 1)), Currency.EUR, 1, 1000);
        Assert.Null(result.Exact); Assert.Null(result.Lower); Plan(result.Upper, 20, (20, 1));
        Assert.Equal(new BanknoteSelection(null, null, null), BanknoteSelector.Select([], Currency.TRY, 1, 100));
    }

    [Fact]
    public void Huge_request_is_bounded_by_stock_not_request_size()
    {
        var result = BanknoteSelector.Select(Stock(Currency.TRY, (500, 100), (500, 100), (500, 100), (500, 100)),
            Currency.TRY, int.MaxValue, int.MaxValue);
        Plan(result.Lower, 200000, (500, 400)); Assert.Null(result.Upper);
    }

    [Theory]
    [InlineData(0, 100, "INVALID_WITHDRAWAL_AMOUNT")]
    [InlineData(-1, 100, "INVALID_WITHDRAWAL_AMOUNT")]
    [InlineData(100, -1, "INVALID_WITHDRAWAL_AMOUNT")]
    [InlineData(100, 99, "DAILY_LIMIT_EXCEEDED")]
    public void Invalid_amounts_and_over_limit_request_are_rejected(int amount, int limit, string code) =>
        Assert.Equal(code, Assert.Throws<DomainException>(() => BanknoteSelector.Select([], Currency.TRY, amount, limit)).Code);

    [Fact]
    public void Invalid_stock_is_rejected_before_allocation()
    {
        var stock = Stock(Currency.TRY, (100, 1));
        foreach (var invalid in new[] { stock[0] with { OnHand = 101 }, stock[0] with { Reserved = 2 },
                     stock[0] with { Reserved = -1 }, stock[0] with { Slot = 5 }, stock[0] with { CassetteId = Guid.Empty } })
            Assert.Equal("INVALID_SELECTION_STOCK", Assert.Throws<DomainException>(() =>
                BanknoteSelector.Select([invalid], Currency.TRY, 100, 1000)).Code);
        Assert.Throws<DomainException>(() => BanknoteSelector.Select([stock[0], stock[0]], Currency.TRY, 100, 1000));
        Assert.Throws<DomainException>(() => BanknoteSelector.Select(stock, (Currency)99, 100, 1000));
    }

    [Fact]
    public void Atm_adapter_excludes_deposit_and_requires_service_without_changing_cash()
    {
        var now = DateTimeOffset.UtcNow;
        var atm = Atm.Create("ATM", now);
        atm.Configure([new(1, Currency.TRY, 100), new(2, Currency.TRY, 50), new(3, Currency.TRY, 20), new(4, Currency.USD, 100)]);
        atm.Load([new(1, Currency.TRY, 100, 1), new(5, Currency.TRY, 20, 100)], "test", now);
        Assert.Throws<DomainException>(() => BanknoteSelector.Select(atm, Currency.TRY, 120, 20000));
        atm.TransitionTo(AtmState.InService);
        var result = BanknoteSelector.Select(atm, Currency.TRY, 120, 20000);
        Plan(result.Lower, 100, (100, 1)); Assert.Null(result.Exact); Assert.Null(result.Upper);
        Assert.Equal(101, atm.Cassettes.Sum(c => c.OnHand));
        Assert.All(atm.Cassettes, c => Assert.Equal(0, c.Reserved));
    }

    [Fact]
    public void Seeded_cases_match_independent_exhaustive_enumeration()
    {
        var random = new Random(20261012);
        for (var sample = 0; sample < 300; sample++)
        {
            var stock = Stock(Currency.TRY, (20, random.Next(4)), (50, random.Next(4)),
                (100, random.Next(4)), (500, random.Next(4)));
            var limit = random.Next(1, 2201);
            var amount = random.Next(1, limit + 1);
            var possible = new Dictionary<int, int>();
            for (var a = 0; a <= stock[0].Available; a++)
            for (var b = 0; b <= stock[1].Available; b++)
            for (var c = 0; c <= stock[2].Available; c++)
            for (var d = 0; d <= stock[3].Available; d++)
            {
                var value = a * 20 + b * 50 + c * 100 + d * 500;
                if (value > 0 && value <= limit)
                    possible[value] = Math.Min(possible.GetValueOrDefault(value, int.MaxValue), a + b + c + d);
            }
            var result = BanknoteSelector.Select(stock, Currency.TRY, amount, limit);
            if (possible.ContainsKey(amount))
            {
                Assert.Equal(amount, result.Exact?.Amount); Assert.Null(result.Lower); Assert.Null(result.Upper);
            }
            else
            {
                Assert.Null(result.Exact);
                Assert.Equal(possible.Keys.Where(v => v < amount).OrderDescending().Cast<int?>().FirstOrDefault(), result.Lower?.Amount);
                Assert.Equal(possible.Keys.Where(v => v > amount).Order().Cast<int?>().FirstOrDefault(), result.Upper?.Amount);
            }
            foreach (var plan in new[] { result.Exact, result.Lower, result.Upper }.OfType<DispensePlan>())
            {
                Assert.Equal(possible[plan.Amount], plan.NoteCount);
                Assert.Equal(plan.Amount, plan.Lines.Sum(l => l.Denomination * l.Count));
                Assert.All(plan.Lines, l => Assert.InRange(l.Count, 1, stock.Single(s => s.CassetteId == l.CassetteId).Available));
            }
        }
    }
}
