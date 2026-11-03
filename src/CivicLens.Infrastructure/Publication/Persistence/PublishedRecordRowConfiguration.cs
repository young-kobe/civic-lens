using CivicLens.Application.Publication;
using CivicLens.Publication.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.Publication.Persistence;

/// <summary>The first publication time of a record. Code only inserts rows. A rollback of a release keeps them.</summary>
internal sealed class PublishedRecordRowConfiguration : IEntityTypeConfiguration<PublishedRecordRow>
{
    public void Configure(EntityTypeBuilder<PublishedRecordRow> builder)
    {
        builder.ToTable("published_records", table =>
        {
            table.HasCheckConstraint("ck_published_records_kind",
                EnumChecks.InList("kind", Enum.GetValues<PublishedRecordKind>().Select(PublishedRecordReference.NameOf)));
            table.HasCheckConstraint("ck_published_records_record_id", RecordIdChecks.AnyRecordId("record_id"));
        });
        builder.HasKey(row => new { row.Kind, row.RecordId });
        builder.Property(row => row.Kind).HasColumnName("kind").HasMaxLength(32).IsRequired();
        builder.Property(row => row.RecordId).HasColumnName("record_id").HasMaxLength(64).IsRequired();
        builder.Property(row => row.FirstPublishedAtUtcTicks).HasColumnName("first_published_at_utc_ticks").IsRequired();
    }
}
