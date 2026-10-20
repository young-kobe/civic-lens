using CivicLens.Application.Roster;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.Roster.Persistence;

internal sealed class ExecutiveTermRowConfiguration : IEntityTypeConfiguration<ExecutiveTermRow>
{
    public void Configure(EntityTypeBuilder<ExecutiveTermRow> builder)
    {
        builder.ToTable("executive_terms", table =>
        {
            table.HasCheckConstraint("ck_executive_terms_office", "office IN ('prez', 'viceprez')");
            table.HasCheckConstraint("ck_executive_terms_dates", "term_index >= 0 AND ends_on >= starts_on");
        });
        builder.HasKey(row => new { row.AttemptId, row.GovtrackId, row.TermIndex });
        builder.Property(row => row.AttemptId).HasColumnName("attempt_id").IsRequired();
        builder.Property(row => row.GovtrackId).HasColumnName("govtrack_id");
        builder.Property(row => row.TermIndex).HasColumnName("term_index");
        builder.Property(row => row.Office).HasColumnName("office").HasMaxLength(8).IsRequired();
        builder.Property(row => row.StartsOn).HasColumnName("starts_on");
        builder.Property(row => row.EndsOn).HasColumnName("ends_on");
        builder.Property(row => row.Party).HasColumnName("party").HasMaxLength(RosterLimits.Party).IsRequired();
        builder.HasOne<ExecutiveOfficialRow>().WithMany().HasForeignKey(row => new { row.AttemptId, row.GovtrackId })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
