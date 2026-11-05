using CivicLens.Publication.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.Publication.Persistence;

internal sealed class ReleasePolicyAreaRowConfiguration : IEntityTypeConfiguration<ReleasePolicyAreaRow>
{
    public void Configure(EntityTypeBuilder<ReleasePolicyAreaRow> builder)
    {
        builder.ToTable("release_policy_areas", table =>
        {
            table.HasCheckConstraint("ck_release_policy_areas_policy_area_id", "length(btrim(policy_area_id)) > 0");
            table.HasCheckConstraint("ck_release_policy_areas_name", "length(btrim(name)) > 0");
        });
        builder.HasKey(row => new { row.ReleaseNumber, row.PolicyAreaId });
        builder.Property(row => row.ReleaseNumber).HasColumnName("release_number");
        builder.Property(row => row.PolicyAreaId).HasColumnName("policy_area_id").HasMaxLength(PublishedRecordEntry.MaximumFilterValueLength);
        builder.Property(row => row.Name).HasColumnName("name").HasMaxLength(PublishedRecordEntry.MaximumFilterValueLength);
        builder.HasOne<PublicationReleaseRow>().WithMany().HasForeignKey(row => row.ReleaseNumber).OnDelete(DeleteBehavior.Restrict);
    }
}
