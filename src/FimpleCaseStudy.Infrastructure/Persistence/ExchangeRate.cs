using FimpleCaseStudy.Domain.Cash;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FimpleCaseStudy.Infrastructure.Persistence;

public sealed class ExchangeRate
{
    private ExchangeRate() { }
    public Currency Currency { get; private set; }
    public decimal RateToTry { get; private set; }
}

internal sealed class ExchangeRateConfiguration : IEntityTypeConfiguration<ExchangeRate>
{
    public void Configure(EntityTypeBuilder<ExchangeRate> builder)
    {
        builder.ToTable("exchange_rates", table => table.HasCheckConstraint("ck_exchange_rate", "\"Currency\" IN ('TRY','USD','EUR') AND \"RateToTry\" > 0 AND \"RateToTry\" <= 999999 AND (\"Currency\" <> 'TRY' OR \"RateToTry\" = 1)"));
        builder.HasKey(r => r.Currency);
        builder.Property(r => r.Currency).HasConversion<string>().HasMaxLength(3).ValueGeneratedNever();
        builder.Property(r => r.RateToTry).HasPrecision(12, 6);
        // Fixed demonstration rates, not live market rates.
        builder.HasData(new { Currency = Currency.TRY, RateToTry = 1m },
            new { Currency = Currency.USD, RateToTry = 40m }, new { Currency = Currency.EUR, RateToTry = 45m });
    }
}
