using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.Collection.Jobs.Persistence;

internal sealed class WorkerHeartbeatRowConfiguration : IEntityTypeConfiguration<WorkerHeartbeatRow>
{
    public void Configure(EntityTypeBuilder<WorkerHeartbeatRow> builder)
    {
        builder.ToTable("worker_heartbeat");
        builder.HasKey(row => row.Loop);
        builder.Property(row => row.Loop).HasColumnName("loop").HasMaxLength(32);
        builder.Property(row => row.BeatAtUtcTicks).HasColumnName("beat_at_utc_ticks");
    }
}
