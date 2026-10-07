using CivicLens.Infrastructure.Collection.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.Collection.Discovery;

internal sealed class DiscoveryRowConfiguration : IEntityTypeConfiguration<DiscoveryRow>
{
    public void Configure(EntityTypeBuilder<DiscoveryRow> builder)
    {
        builder.ToTable("collection_discoveries");
        builder.HasKey(row => row.AttemptId);
        builder.Property(row => row.AttemptId).HasColumnName("attempt_id");
        builder.Property(row => row.SourceId).HasColumnName("source_id").IsRequired();
        builder.Property(row => row.RequestJson).HasColumnName("request_json").IsRequired();
        builder.Property(row => row.DiscoveryJson).HasColumnName("discovery_json").IsRequired();
        builder.HasOne<AttemptRow>().WithOne().HasForeignKey<DiscoveryRow>(row => row.AttemptId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
