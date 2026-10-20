using CivicLens.Infrastructure.Collection.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.Roster.Persistence;

internal sealed class RosterSnapshotRowConfiguration : IEntityTypeConfiguration<RosterSnapshotRow>
{
    public void Configure(EntityTypeBuilder<RosterSnapshotRow> builder)
    {
        builder.ToTable("roster_snapshots");
        builder.HasKey(row => row.AttemptId);
        builder.Property(row => row.AttemptId).HasColumnName("attempt_id");
        builder.Property(row => row.SourceId).HasColumnName("source_id").IsRequired();
        builder.HasOne<AttemptRow>().WithMany().HasForeignKey(row => row.AttemptId).OnDelete(DeleteBehavior.Restrict);
    }
}
