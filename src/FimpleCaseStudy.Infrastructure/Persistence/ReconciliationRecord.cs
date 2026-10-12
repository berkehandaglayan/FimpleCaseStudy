using System.Text.Json;
using FimpleCaseStudy.Application.Atms;
using FimpleCaseStudy.Domain.Cash;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FimpleCaseStudy.Infrastructure.Persistence;

public sealed class ReconciliationRecord
{
    private ReconciliationRecord() { }
    public Guid Id { get; private set; }
    public Guid AtmId { get; private set; }
    public string Actor { get; private set; } = null!;
    public DateTimeOffset OccurredAt { get; private set; }
    public string LinesJson { get; private set; } = null!;
    public ReconciliationStatus Status { get; private set; }
    public decimal? TotalDifferenceTry { get; private set; }
    public string? RatesJson { get; private set; }
    public string? ApprovedBy { get; private set; }
    public DateTimeOffset? ApprovedAt { get; private set; }

    internal static ReconciliationRecord From(ReconciliationReceipt receipt, IReadOnlyList<ExchangeRateSnapshot> rates)
    {
        var difference = ReconciliationApproval.DifferenceTry(receipt.Lines, rates);
        return new()
        {
            Id = receipt.OperationId, AtmId = receipt.AtmId, Actor = receipt.Actor,
            OccurredAt = receipt.OccurredAt, LinesJson = JsonSerializer.Serialize(receipt.Lines),
            TotalDifferenceTry = difference, RatesJson = JsonSerializer.Serialize(rates),
            Status = ReconciliationApproval.StatusFor(difference)
        };
    }
    internal ReconciliationView ToView() => new(Id, AtmId, Actor, OccurredAt,
        JsonSerializer.Deserialize<ReconciliationLine[]>(LinesJson)!, Status, TotalDifferenceTry,
        RatesJson is null ? null : JsonSerializer.Deserialize<ExchangeRateSnapshot[]>(RatesJson), ApprovedBy, ApprovedAt);

    internal void Approve(string actor, DateTimeOffset now)
    {
        if (!ReconciliationApproval.ValidateApproval(Status, Actor, ApprovedBy, actor)) return;
        Status = ReconciliationStatus.Approved;
        ApprovedBy = actor;
        ApprovedAt = now.ToUniversalTime();
    }
}

internal sealed class ReconciliationConfiguration : IEntityTypeConfiguration<ReconciliationRecord>
{
    public void Configure(EntityTypeBuilder<ReconciliationRecord> builder)
    {
        builder.ToTable("reconciliations", table =>
        {
            table.HasCheckConstraint("ck_reconciliation_actor", "length(btrim(\"Actor\")) > 0");
            table.HasCheckConstraint("ck_reconciliation_review", """
                ("Status" = 'LegacyCompleted' AND "TotalDifferenceTry" IS NULL AND "RatesJson" IS NULL AND "ApprovedBy" IS NULL AND "ApprovedAt" IS NULL)
                OR ("TotalDifferenceTry" IS NOT NULL AND "RatesJson" IS NOT NULL AND (
                    ("Status" = 'Completed' AND "TotalDifferenceTry" BETWEEN 0 AND 1000 AND "ApprovedBy" IS NULL AND "ApprovedAt" IS NULL)
                    OR ("Status" = 'AwaitingApproval' AND "TotalDifferenceTry" > 1000 AND "ApprovedBy" IS NULL AND "ApprovedAt" IS NULL)
                    OR ("Status" = 'Approved' AND "TotalDifferenceTry" > 1000 AND "ApprovedBy" IS NOT NULL AND "ApprovedAt" IS NOT NULL
                        AND length(btrim("ApprovedBy")) > 0 AND lower(btrim("ApprovedBy")) <> lower(btrim("Actor")) AND "ApprovedAt" >= "OccurredAt")))
                """);
        });
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.Actor).HasMaxLength(128).IsRequired();
        builder.Property(r => r.LinesJson).HasColumnType("jsonb").IsRequired();
        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(24);
        builder.Property(r => r.TotalDifferenceTry).HasPrecision(20, 6);
        builder.Property(r => r.RatesJson).HasColumnType("jsonb");
        builder.Property(r => r.ApprovedBy).HasMaxLength(128);
        builder.HasIndex(r => new { r.AtmId, r.OccurredAt });
        builder.HasIndex(r => new { r.AtmId, r.Status });
        builder.HasOne<Atm>().WithMany().HasForeignKey(r => r.AtmId).OnDelete(DeleteBehavior.Restrict);
    }
}
