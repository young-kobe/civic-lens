using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.Analysis.Persistence;

internal sealed class DocumentChangeAnalysisRunRowConfiguration : IEntityTypeConfiguration<DocumentChangeAnalysisRunRow>
{
    public void Configure(EntityTypeBuilder<DocumentChangeAnalysisRunRow> builder)
    {
        AnalysisTableConfiguration.ConfigureRun(builder, "document_change_analysis_runs", nameof(DocumentChangeAnalysisRunRow.ComparisonId),
            "comparison_id", table =>
            {
                table.HasCheckConstraint("ck_document_change_analysis_runs_ids",
                    "run_id ~ '^[0-9a-f]{32}$' AND input_hash ~ '^[0-9a-f]{64}$' AND (previous_run_id IS NULL OR previous_run_id ~ '^[0-9a-f]{32}$')");
                table.HasCheckConstraint("ck_document_change_analysis_runs_draft",
                    "(draft_id IS NULL AND revision_number IS NULL) OR (draft_id IS NOT NULL AND revision_number = 1)");
                table.HasCheckConstraint("ck_document_change_analysis_runs_outcome",
                    "outcome IN ('Drafted','DraftDiscarded','CitationRejected','OutputRejected','Refused','OutputLimitReached','UnexpectedStop','RateLimited','ProviderUnavailable','AuthenticationFailed','ProviderRejected','ConnectionFailed','BillingFailed','ModelNotFound','ProcessingFailed','InvalidResponse','Interrupted')");
            });
        builder.Property(row => row.DraftId).HasColumnName("draft_id").HasMaxLength(32);
        builder.Property(row => row.RevisionNumber).HasColumnName("revision_number");
        builder.HasOne<DocumentChangeAnalysisRow>().WithMany()
            .HasForeignKey(row => new { row.ComparisonId, row.Task }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Review.Persistence.DocumentChangeRevisionRow>().WithMany()
            .HasForeignKey(row => new { row.DraftId, row.RevisionNumber }).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(row => new { row.DraftId, row.RevisionNumber }).IsUnique();
    }
}
