using CivicLens.Application.Roster;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.Roster.Persistence;

internal sealed class ExecutiveOfficialRowConfiguration : IEntityTypeConfiguration<ExecutiveOfficialRow>
{
    public void Configure(EntityTypeBuilder<ExecutiveOfficialRow> builder)
    {
        builder.ToTable("executive_officials", table =>
        {
            table.HasCheckConstraint("ck_executive_officials_identity",
                "govtrack_id > 0 AND (wikidata_id IS NULL OR wikidata_id ~ '^Q[1-9][0-9]*$') AND length(btrim(full_name)) > 0");
            table.HasCheckConstraint("ck_executive_officials_last_name", "last_name IS NULL OR length(btrim(last_name)) > 0");
        });
        builder.HasKey(row => new { row.AttemptId, row.GovtrackId });
        builder.Property(row => row.AttemptId).HasColumnName("attempt_id").IsRequired();
        builder.Property(row => row.GovtrackId).HasColumnName("govtrack_id");
        builder.Property(row => row.WikidataId).HasColumnName("wikidata_id").HasMaxLength(RosterLimits.WikidataId);
        builder.Property(row => row.FullName).HasColumnName("full_name").HasMaxLength(RosterLimits.FullName).IsRequired();
        builder.Property(row => row.LastName).HasColumnName("last_name").HasMaxLength(RosterLimits.FullName);
        builder.HasOne<RosterSnapshotRow>().WithMany().HasForeignKey(row => row.AttemptId).OnDelete(DeleteBehavior.Restrict);
    }
}
