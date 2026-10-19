using CivicLens.Application.Roster;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.Roster.Persistence;

internal sealed class RosterTermRowConfiguration : IEntityTypeConfiguration<RosterTermRow>
{
    public void Configure(EntityTypeBuilder<RosterTermRow> builder)
    {
        builder.ToTable("roster_terms", table =>
        {
            table.HasCheckConstraint("ck_roster_terms_chamber", "chamber IN ('sen', 'rep')");
            table.HasCheckConstraint("ck_roster_terms_dates", "term_index >= 0 AND ends_on >= starts_on");
        });
        builder.HasKey(row => new { row.AttemptId, row.BioguideId, row.TermIndex });
        builder.Property(row => row.AttemptId).HasColumnName("attempt_id").IsRequired();
        builder.Property(row => row.BioguideId).HasColumnName("bioguide_id").HasMaxLength(7);
        builder.Property(row => row.TermIndex).HasColumnName("term_index");
        builder.Property(row => row.Chamber).HasColumnName("chamber").HasMaxLength(3).IsRequired();
        builder.Property(row => row.StartsOn).HasColumnName("starts_on");
        builder.Property(row => row.EndsOn).HasColumnName("ends_on");
        builder.Property(row => row.State).HasColumnName("state").HasMaxLength(RosterLimits.State).IsRequired();
        builder.Property(row => row.District).HasColumnName("district");
        builder.Property(row => row.SenateClass).HasColumnName("senate_class");
        builder.Property(row => row.Party).HasColumnName("party").HasMaxLength(RosterLimits.Party).IsRequired();
        builder.HasOne<RosterMemberRow>().WithMany().HasForeignKey(row => new { row.AttemptId, row.BioguideId })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
