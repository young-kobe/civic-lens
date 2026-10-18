using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.Relationships.Persistence;

internal sealed class RelationshipRevisionRowConfiguration : IEntityTypeConfiguration<RelationshipRevisionRow>
{
    public void Configure(EntityTypeBuilder<RelationshipRevisionRow> builder)
    {
        builder.ToTable("relationship_revisions", table =>
        {
            table.HasCheckConstraint("ck_relationship_revisions_number", "revision_number >= 1");
            table.HasCheckConstraint("ck_relationship_revisions_author_kind", "author_kind IN ('Person','Analysis')");
            table.HasCheckConstraint("ck_relationship_revisions_run", "(author_kind = 'Analysis') = (run_id IS NOT NULL)");
        });
        builder.HasKey(row => new { row.RecordId, row.RevisionNumber });
        builder.Property(row => row.RecordId).HasColumnName("record_id").HasMaxLength(64);
        builder.Property(row => row.RevisionNumber).HasColumnName("revision_number");
        builder.Property(row => row.AuthorSubject).HasColumnName("author_subject").HasMaxLength(256).IsRequired();
        builder.Property(row => row.AuthorKind).HasColumnName("author_kind").HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(row => row.RunId).HasColumnName("run_id").HasMaxLength(32);
        builder.Property(row => row.RevisionJson).HasColumnName("revision_json").IsRequired();
        builder.Property(row => row.CreatedAtUtcTicks).HasColumnName("created_at_utc_ticks");
        builder.HasIndex(row => row.RunId).IsUnique();
        builder.HasOne<RelationshipDraftRow>().WithMany().HasForeignKey(row => row.RecordId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<RelationshipAnalysisRunRow>().WithMany().HasForeignKey(row => row.RunId).OnDelete(DeleteBehavior.Restrict);
    }
}
