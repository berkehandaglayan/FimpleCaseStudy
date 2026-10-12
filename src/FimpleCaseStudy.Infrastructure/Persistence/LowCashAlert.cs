using FimpleCaseStudy.Application.Atms;
using FimpleCaseStudy.Domain.Cash;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FimpleCaseStudy.Infrastructure.Persistence;

public sealed class LowCashAlert
{
    private LowCashAlert() { }
    public Guid Id { get; private set; }
    public Guid AtmId { get; private set; }
    public Guid CassetteId { get; private set; }
    public int Slot { get; private set; }
    public Currency Currency { get; private set; }
    public int Denomination { get; private set; }
    public int BeforeAvailable { get; private set; }
    public int Available { get; private set; }
    public int Threshold { get; private set; }
    public Guid OperationId { get; private set; }
    public string Reason { get; private set; } = null!;
    public DateTimeOffset OccurredAt { get; private set; }
    public int Attempts { get; private set; }
    public DateTimeOffset NextAttemptAt { get; private set; }
    public DateTimeOffset? SentAt { get; private set; }

    internal static LowCashAlert Create(Atm atm, Cassette cassette, int before, Guid operation,
        bool firstService, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(), AtmId = atm.Id, CassetteId = cassette.Id, Slot = cassette.Slot,
        Currency = cassette.Currency!.Value, Denomination = cassette.Denomination!.Value,
        BeforeAvailable = before, Available = cassette.Available, Threshold = LowCashPolicy.Threshold,
        OperationId = operation, Reason = firstService ? "INITIAL_SERVICE_LOW_CASH" : "AVAILABLE_COUNT_FELL_BELOW_THRESHOLD",
        OccurredAt = now, NextAttemptAt = now
    };

    internal void Attempt() => Attempts++;
    internal void MarkSent(DateTimeOffset now) => SentAt = now;
    internal void RetryAt(DateTimeOffset now) => NextAttemptAt = now.AddSeconds(5);
    internal LowCashAlertView ToView() => new(Id, AtmId, CassetteId, Slot, Currency, Denomination,
        BeforeAvailable, Available, Threshold, OperationId, Reason, OccurredAt, Attempts, SentAt);
}

internal sealed class LowCashAlertConfiguration : IEntityTypeConfiguration<LowCashAlert>
{
    public void Configure(EntityTypeBuilder<LowCashAlert> builder)
    {
        builder.ToTable("outbox", table =>
        {
            table.HasCheckConstraint("ck_outbox_note", "\"Slot\" BETWEEN 1 AND 4 AND \"Currency\" IN ('TRY','USD','EUR') AND \"Denomination\" IN (20,50,100,500)");
            table.HasCheckConstraint("ck_outbox_counts", "\"BeforeAvailable\" BETWEEN 0 AND 100 AND \"Available\" BETWEEN 0 AND 9 AND \"Threshold\" = 10 AND \"Attempts\" >= 0");
            table.HasCheckConstraint("ck_outbox_reason", "\"Reason\" IN ('INITIAL_SERVICE_LOW_CASH','AVAILABLE_COUNT_FELL_BELOW_THRESHOLD')");
            table.HasCheckConstraint("ck_outbox_delivery", "\"NextAttemptAt\" >= \"OccurredAt\" AND (\"SentAt\" IS NULL OR \"SentAt\" >= \"OccurredAt\")");
        });
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Currency).HasConversion<string>().HasMaxLength(3);
        builder.Property(e => e.Reason).HasMaxLength(64);
        builder.HasIndex(e => new { e.OperationId, e.CassetteId }).IsUnique();
        builder.HasIndex(e => new { e.AtmId, e.OccurredAt });
        builder.HasIndex(e => new { e.NextAttemptAt, e.OccurredAt }).HasFilter("\"SentAt\" IS NULL");
        builder.HasOne<Cassette>().WithMany().HasForeignKey(e => new { e.CassetteId, e.AtmId })
            .HasPrincipalKey(c => new { c.Id, c.AtmId }).OnDelete(DeleteBehavior.Restrict);
    }
}
