using CivicLens.Core.Review;
using CivicLens.Infrastructure.Documents.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.Review.Persistence;

internal sealed class DocumentChangeRevisionRowConfiguration : IEntityTypeConfiguration<DocumentChangeRevisionRow>
{
    private static readonly string[] WrittenTogether = ["comparison_id", "headline", "summary", "institution"];

    public void Configure(EntityTypeBuilder<DocumentChangeRevisionRow> builder)
    {
        builder.ToTable("document_change_revisions", table =>
        {
            table.HasCheckConstraint("ck_document_change_revisions_number", "revision_number >= 1");
            table.HasCheckConstraint("ck_document_change_revisions_author_kind", EnumChecks.InList("author_kind", Enum.GetNames<ReviewAuthorKind>()));
            table.HasCheckConstraint("ck_document_change_revisions_comparison", "comparison_id ~ '^[0-9a-f]{64}$'");
            table.HasCheckConstraint("ck_document_change_revisions_significance", $"length(significance) <= {DocumentChangeDraftRevision.MaximumSignificanceLength}");
            table.HasCheckConstraint("ck_document_change_revisions_limits", $"length(limits) <= {DocumentChangeDraftRevision.MaximumLimitsLength}");
            table.HasCheckConstraint("ck_document_change_revisions_columns_written", $"num_nulls({string.Join(", ", WrittenTogether)}) IN (0, {WrittenTogether.Length})");
        });
        builder.HasKey(row => new { row.DraftId, row.RevisionNumber });
        builder.Property(row => row.DraftId).HasColumnName("draft_id").HasMaxLength(32);
        builder.Property(row => row.RevisionNumber).HasColumnName("revision_number");
        builder.Property(row => row.RevisionJson).HasColumnName("revision_json").IsRequired();
        builder.Property(row => row.CreatedAtUtcTicks).HasColumnName("created_at_utc_ticks").IsRequired();
        builder.Property(row => row.AuthorSubject).HasColumnName("author_subject").HasMaxLength(ReviewAuthor.MaximumSubjectLength).IsRequired();
        builder.Property(row => row.AuthorKind).HasColumnName("author_kind").HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(row => row.ComparisonId).HasColumnName("comparison_id").HasMaxLength(64);
        builder.Property(row => row.Significance).HasColumnName("significance");
        builder.Property(row => row.Limits).HasColumnName("limits");
        builder.Property(row => row.Institution).HasColumnName("institution").HasMaxLength(DocumentChangeDraftRevision.MaximumInstitutionLength);
        builder.Property(row => row.ChangeDate).HasColumnName("change_date").HasColumnType("date");
        HeadlineSummaryColumns.Configure(builder, "document_change_revisions", DocumentChangeDraftRevision.MaximumHeadlineLength,
            DocumentChangeDraftRevision.MaximumSummaryLength, allowEmpty: true);
        builder.HasOne<DocumentChangeDraftRow>().WithMany().HasForeignKey(row => row.DraftId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<DocumentChangeDraftRow>().WithMany().HasForeignKey(row => new { row.DraftId, row.ComparisonId })
            .HasPrincipalKey(draft => new { draft.DraftId, draft.ComparisonId }).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(row => row.Officials).WithOne().HasForeignKey(official => new { official.DraftId, official.RevisionNumber }).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(row => row.Issues).WithOne().HasForeignKey(issue => new { issue.DraftId, issue.RevisionNumber }).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(row => row.Citations).WithOne().HasForeignKey(citation => new { citation.DraftId, citation.RevisionNumber }).OnDelete(DeleteBehavior.Restrict);
    }
}
