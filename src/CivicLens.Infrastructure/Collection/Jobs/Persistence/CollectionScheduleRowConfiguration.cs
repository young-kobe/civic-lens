using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.Collection.Jobs.Persistence;

internal sealed class CollectionScheduleRowConfiguration : IEntityTypeConfiguration<CollectionScheduleRow>
{
    public void Configure(EntityTypeBuilder<CollectionScheduleRow> builder)
    {
        builder.ToTable("collection_source_schedules", table =>
        {
            table.HasCheckConstraint("ck_collection_source_schedules_revision", "configuration_revision_id ~ '^[0-9a-f]{64}$'");
            table.HasCheckConstraint("ck_collection_source_schedules_interval", "interval_seconds BETWEEN 60 AND 604800");
            table.HasCheckConstraint("ck_collection_source_schedules_due", "next_due_utc_ticks BETWEEN 0 AND 3155378975999999999");
        });
        builder.Property(row => row.SourceId).HasColumnName("source_id").HasMaxLength(128);
        builder.Property(row => row.ConfigurationRevisionId).HasColumnName("configuration_revision_id").HasMaxLength(64);
        builder.Property(row => row.ConfigurationJson).HasColumnName("configuration_json").IsRequired();
        builder.Property(row => row.IntervalSeconds).HasColumnName("interval_seconds");
        builder.Property(row => row.NextDueUtcTicks).HasColumnName("next_due_utc_ticks");
        builder.Property(row => row.Enabled).HasColumnName("enabled");
        builder.HasKey(row => row.SourceId);
        builder.HasIndex(row => new { row.Enabled, row.NextDueUtcTicks, row.SourceId });
    }
}
