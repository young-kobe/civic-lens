using CivicLens.Application.Roster;
using CivicLens.Infrastructure.Collection.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.Roster.Persistence;

internal sealed class RosterMemberRowConfiguration : IEntityTypeConfiguration<RosterMemberRow>
{
    public void Configure(EntityTypeBuilder<RosterMemberRow> builder)
    {
        builder.ToTable("roster_members", table =>
        {
            table.HasCheckConstraint("ck_roster_members_identity",
                "bioguide_id ~ '^[A-Z][0-9]{6}$' AND length(btrim(full_name)) > 0");
        });
        builder.HasKey(row => new { row.AttemptId, row.BioguideId });
        builder.Property(row => row.AttemptId).HasColumnName("attempt_id").IsRequired();
        builder.Property(row => row.BioguideId).HasColumnName("bioguide_id").HasMaxLength(7);
        builder.Property(row => row.LisId).HasColumnName("lis_id").HasMaxLength(RosterLimits.LisId);
        builder.Property(row => row.FullName).HasColumnName("full_name").HasMaxLength(RosterLimits.FullName).IsRequired();
        builder.Property(row => row.Office).HasColumnName("office").HasMaxLength(RosterLimits.Office);
        builder.Property(row => row.Phone).HasColumnName("phone").HasMaxLength(RosterLimits.Phone);
        builder.Property(row => row.Url).HasColumnName("url").HasMaxLength(RosterLimits.Url);
        builder.HasOne<AttemptRow>().WithMany().HasForeignKey(row => row.AttemptId).OnDelete(DeleteBehavior.Restrict);
    }
}
