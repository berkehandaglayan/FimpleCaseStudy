using FimpleCaseStudy.Domain.Cash;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FimpleCaseStudy.Infrastructure.Persistence;

internal sealed class AtmConfiguration : IEntityTypeConfiguration<Atm>
{
    public void Configure(EntityTypeBuilder<Atm> builder)
    {
        builder.ToTable("atms", table =>
        {
            table.HasCheckConstraint("ck_atm_state", "\"State\" IN ('Unconfigured','Configured','Loaded','InService','Maintenance')");
            table.HasCheckConstraint("ck_atm_name", "length(btrim(\"Name\")) > 0");
        });
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();
        builder.Property(a => a.Name).HasMaxLength(100).IsRequired();
        builder.Property(a => a.State).HasConversion<string>().HasMaxLength(20);
        builder.HasMany(a => a.Cassettes).WithOne().HasForeignKey(c => c.AtmId).OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(a => a.Cassettes).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class CassetteConfigurationMapping : IEntityTypeConfiguration<Cassette>
{
    public void Configure(EntityTypeBuilder<Cassette> builder)
    {
        builder.ToTable("cassettes", table =>
        {
            table.HasCheckConstraint("ck_cassette_slot_kind", "(\"Slot\" BETWEEN 1 AND 4 AND \"Kind\" = 'Recycling') OR (\"Slot\" = 5 AND \"Kind\" = 'Deposit')");
            table.HasCheckConstraint("ck_cassette_configuration", "(\"Currency\" IS NULL AND \"Denomination\" IS NULL) OR (\"Kind\" = 'Recycling' AND \"Currency\" IS NOT NULL AND \"Denomination\" IS NOT NULL AND \"Currency\" IN ('TRY','USD','EUR') AND \"Denomination\" IN (20,50,100,500))");
        });
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.HasAlternateKey(c => new { c.Id, c.AtmId });
        builder.HasIndex(c => new { c.AtmId, c.Slot }).IsUnique();
        builder.Property(c => c.Kind).HasConversion<string>().HasMaxLength(20);
        builder.Property(c => c.Currency).HasConversion<string>().HasMaxLength(3);
        builder.Ignore(c => c.OnHand);
        builder.Ignore(c => c.Reserved);
        builder.Ignore(c => c.Available);
        builder.HasMany(c => c.Balances).WithOne().HasForeignKey(b => b.CassetteId).OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(c => c.Balances).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class BalanceConfiguration : IEntityTypeConfiguration<CassetteBalance>
{
    public void Configure(EntityTypeBuilder<CassetteBalance> builder)
    {
        builder.ToTable("balances", table =>
        {
            table.HasCheckConstraint("ck_balance_counts", "\"OnHand\" BETWEEN 0 AND 100 AND \"Reserved\" BETWEEN 0 AND \"OnHand\"");
            table.HasCheckConstraint("ck_balance_note", "\"Currency\" IN ('TRY','USD','EUR') AND \"Denomination\" IN (20,50,100,500)");
        });
        builder.HasKey(b => new { b.CassetteId, b.Currency, b.Denomination });
        builder.Property(b => b.Currency).HasConversion<string>().HasMaxLength(3);
        builder.Ignore(b => b.Available);
    }
}

internal sealed class MovementConfiguration : IEntityTypeConfiguration<CashMovement>
{
    public void Configure(EntityTypeBuilder<CashMovement> builder)
    {
        builder.ToTable("movements", table =>
        {
            table.HasCheckConstraint("ck_movement_note", "\"Currency\" IN ('TRY','USD','EUR') AND \"Denomination\" IN (20,50,100,500)");
            table.HasCheckConstraint("ck_movement_delta", "\"PhysicalDelta\" BETWEEN -100 AND 100 AND \"ReservedDelta\" BETWEEN -100 AND 100 AND (\"PhysicalDelta\" <> 0 OR \"ReservedDelta\" <> 0)");
            table.HasCheckConstraint("ck_movement_type", "\"Type\" IN ('Load','Unload','Withdrawal','Deposit','Reservation','ReservationRelease','ReconciliationAdjustment')");
        });
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).ValueGeneratedNever();
        builder.Property(m => m.Currency).HasConversion<string>().HasMaxLength(3);
        builder.Property(m => m.Type).HasConversion<string>().HasMaxLength(30);
        builder.Property(m => m.Actor).HasMaxLength(128).IsRequired();
        builder.HasIndex(m => new { m.AtmId, m.OccurredAt });
        builder.HasIndex(m => m.OperationId);
        builder.HasOne<Cassette>().WithMany().HasForeignKey(m => new { m.CassetteId, m.AtmId })
            .HasPrincipalKey(c => new { c.Id, c.AtmId }).OnDelete(DeleteBehavior.Restrict);
    }
}
