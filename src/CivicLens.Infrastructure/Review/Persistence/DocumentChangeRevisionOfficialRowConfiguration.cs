using CivicLens.Core.Review;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.Review.Persistence;

internal sealed class DocumentChangeRevisionOfficialRowConfiguration : IEntityTypeConfiguration<DocumentChangeRevisionOfficialRow>
{
    public void Configure(EntityTypeBuilder<DocumentChangeRevisionOfficialRow> builder)
    {
        builder.ToTable("document_change_revision_officials", table =>
        {
            table.HasCheckConstraint("ck_document_change_revision_officials_position", $"position BETWEEN 0 AND {DocumentChangeDraftRevision.MaximumSelections - 1}");
            table.HasCheckConstraint("ck_document_change_revision_officials_official", "length(btrim(official_id)) > 0");
        });
        builder.HasKey(row => new { row.DraftId, row.RevisionNumber, row.OfficialId });
        builder.Property(row => row.DraftId).HasColumnName("draft_id").HasMaxLength(32);
        builder.Property(row => row.RevisionNumber).HasColumnName("revision_number");
        builder.Property(row => row.OfficialId).HasColumnName("official_id").HasMaxLength(ReviewTextRules.MaximumConfigurationIdLength);
        builder.Property(row => row.Position).HasColumnName("position");
    }
}
