using System.Text.Json;
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

    internal static ReconciliationRecord From(ReconciliationReceipt receipt) => new()
    {
        Id = receipt.OperationId, AtmId = receipt.AtmId, Actor = receipt.Actor,
        OccurredAt = receipt.OccurredAt, LinesJson = JsonSerializer.Serialize(receipt.Lines)
    };
    internal ReconciliationReceipt ToReceipt() => new(Id, AtmId, Actor, OccurredAt,
        JsonSerializer.Deserialize<ReconciliationLine[]>(LinesJson)!);
}

internal sealed class ReconciliationConfiguration : IEntityTypeConfiguration<ReconciliationRecord>
{
    public void Configure(EntityTypeBuilder<ReconciliationRecord> builder)
    {
        builder.ToTable("reconciliations", table => table.HasCheckConstraint("ck_reconciliation_actor", "length(btrim(\"Actor\")) > 0"));
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.Actor).HasMaxLength(128).IsRequired();
        builder.Property(r => r.LinesJson).HasColumnType("jsonb").IsRequired();
        builder.HasIndex(r => new { r.AtmId, r.OccurredAt });
        builder.HasOne<Atm>().WithMany().HasForeignKey(r => r.AtmId).OnDelete(DeleteBehavior.Restrict);
    }
}
