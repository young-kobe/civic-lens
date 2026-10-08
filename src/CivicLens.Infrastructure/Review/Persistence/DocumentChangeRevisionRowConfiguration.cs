using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.Review.Persistence;

internal sealed class DocumentChangeRevisionRowConfiguration : IEntityTypeConfiguration<DocumentChangeRevisionRow>
{
    public void Configure(EntityTypeBuilder<DocumentChangeRevisionRow> builder)
    {
        builder.ToTable("document_change_revisions", table =>
            table.HasCheckConstraint("ck_document_change_revisions_number", "revision_number >= 1"));
        builder.HasKey(row => new { row.DraftId, row.RevisionNumber });
        builder.Property(row => row.DraftId).HasColumnName("draft_id").HasMaxLength(32);
        builder.Property(row => row.RevisionNumber).HasColumnName("revision_number");
        builder.Property(row => row.RevisionJson).HasColumnName("revision_json").IsRequired();
        builder.HasOne<DocumentChangeDraftRow>().WithMany().HasForeignKey(row => row.DraftId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
