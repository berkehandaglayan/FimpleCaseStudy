using FimpleCaseStudy.Domain.Cash;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FimpleCaseStudy.Infrastructure.Persistence;

public sealed class WithdrawalQuote
{
    public Guid Id { get; set; }
    public Guid AtmId { get; set; }
    public string CardHash { get; set; } = "";
    public Currency Currency { get; set; }
    public string Status { get; set; } = "Open";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? ConfirmedAt { get; set; }
    public int ConfirmedAmount { get; set; }
    public int LimitReservation { get; set; }
    public string OptionsJson { get; set; } = "[]";
    public string ReservationsJson { get; set; } = "[]";
    public string? SelectedOption { get; set; }
}

public sealed class IdempotencyRecord
{
    public string Key { get; set; } = "";
    public string Fingerprint { get; set; } = "";
    public int StatusCode { get; set; }
    public string ResponseJson { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}

internal sealed class WithdrawalQuoteConfiguration : IEntityTypeConfiguration<WithdrawalQuote>
{
    public void Configure(EntityTypeBuilder<WithdrawalQuote> b)
    {
        b.ToTable("withdrawal_quotes", t =>
        {
            t.HasCheckConstraint("ck_quote_status", "\"Status\" IN ('Open','Confirmed','Cancelled','Expired')");
            t.HasCheckConstraint("ck_quote_currency", "\"Currency\" IN ('TRY','USD','EUR')");
            t.HasCheckConstraint("ck_quote_amounts", "\"LimitReservation\" > 0 AND \"ConfirmedAmount\" >= 0");
            t.HasCheckConstraint("ck_quote_expiration", "\"ExpiresAt\" = \"CreatedAt\" + interval '60 seconds'");
            t.HasCheckConstraint("ck_quote_confirmation", "(\"Status\" = 'Confirmed' AND \"ConfirmedAt\" IS NOT NULL AND \"ConfirmedAmount\" > 0 AND \"SelectedOption\" IS NOT NULL) OR (\"Status\" <> 'Confirmed' AND \"ConfirmedAt\" IS NULL AND \"ConfirmedAmount\" = 0 AND \"SelectedOption\" IS NULL)");
        });
        b.HasKey(q => q.Id);
        b.Property(q => q.Id).ValueGeneratedNever();
        b.Property(q => q.CardHash).HasMaxLength(64).IsRequired();
        b.Property(q => q.Currency).HasConversion<string>().HasMaxLength(3);
        b.Property(q => q.Status).HasMaxLength(12).IsRequired();
        b.Property(q => q.SelectedOption).HasMaxLength(8);
        b.Property(q => q.OptionsJson).IsRequired();
        b.Property(q => q.ReservationsJson).IsRequired();
        b.HasOne<Atm>().WithMany().HasForeignKey(q => q.AtmId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(q => new { q.Status, q.ExpiresAt });
        b.HasIndex(q => new { q.CardHash, q.Currency, q.ConfirmedAt });
    }
}

internal sealed class IdempotencyConfiguration : IEntityTypeConfiguration<IdempotencyRecord>
{
    public void Configure(EntityTypeBuilder<IdempotencyRecord> b)
    {
        b.ToTable("idempotency_records");
        b.HasKey(r => r.Key);
        b.Property(r => r.Key).HasMaxLength(128);
        b.Property(r => r.Fingerprint).HasMaxLength(64).IsRequired();
        b.Property(r => r.ResponseJson).IsRequired();
    }
}
