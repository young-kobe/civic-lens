using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.Collection.Jobs.Persistence;

internal sealed class ConfigurationRevisionRowConfiguration : IEntityTypeConfiguration<ConfigurationRevisionRow>
{
    public void Configure(EntityTypeBuilder<ConfigurationRevisionRow> builder)
    {
        builder.ToTable("collection_configuration_revisions", table =>
            table.HasCheckConstraint("ck_collection_configuration_revisions_id", "revision_id ~ '^[0-9a-f]{64}$'"));
        builder.Property(row => row.RevisionId).HasColumnName("revision_id").HasMaxLength(64);
        builder.Property(row => row.Json).HasColumnName("json").IsRequired();
        builder.Property(row => row.CreatedAt).HasColumnName("created_at");
        builder.HasKey(row => row.RevisionId);
    }
}
