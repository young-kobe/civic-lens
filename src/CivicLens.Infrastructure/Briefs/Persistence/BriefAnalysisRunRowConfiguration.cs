using CivicLens.Application.Briefs;
using CivicLens.Infrastructure.Analysis.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.Briefs.Persistence;

internal sealed class BriefAnalysisRunRowConfiguration : IEntityTypeConfiguration<BriefAnalysisRunRow>
{
    private static readonly string CheckedRunRule =
        "(checked_revision_number IS NULL) = (recommendation IS NULL) AND (checked_revision_number IS NULL OR " +
        $"(checked_revision_number >= 1 AND task = '{BriefCheckTask.Task}' AND recommendation IN (" +
        string.Join(",", Enum.GetNames<BriefCheckRecommendation>().Select(name => $"'{name}'")) + "))) AND " +
        $"(source_run_id IS NOT NULL) = (task = '{BriefCheckTask.Task}' AND outcome = 'Bypassed')";

    public void Configure(EntityTypeBuilder<BriefAnalysisRunRow> builder)
    {
        AnalysisTableConfiguration.ConfigureRun(builder, "brief_analysis_runs", nameof(BriefAnalysisRunRow.RecordId),
            "record_id", table =>
            {
                table.HasCheckConstraint("ck_brief_analysis_runs_ids",
                    "run_id ~ '^[0-9a-f]{32}$' AND input_hash ~ '^[0-9a-f]{64}$' AND (previous_run_id IS NULL OR previous_run_id ~ '^[0-9a-f]{32}$')");
                table.HasCheckConstraint("ck_brief_analysis_runs_reason",
                    "reason IS NULL OR reason IN ('unchanged','noNewRecords')");
                table.HasCheckConstraint("ck_brief_analysis_runs_check", CheckedRunRule);
                table.HasCheckConstraint("ck_brief_analysis_runs_fingerprint",
                    $"(input_fingerprint IS NOT NULL) = (task = '{BriefCheckTask.Task}' AND outcome = 'Drafted') AND " +
                    "(input_fingerprint IS NULL OR input_fingerprint ~ '^[0-9a-f]{64}$')");
                table.HasCheckConstraint("ck_brief_analysis_runs_outcome",
                    "outcome IN ('Drafted','DraftDiscarded','CitationRejected','OutputRejected','Refused','OutputLimitReached','UnexpectedStop','RateLimited','ProviderUnavailable','AuthenticationFailed','ProviderRejected','ConnectionFailed','BillingFailed','ModelNotFound','ProcessingFailed','InvalidResponse','Interrupted','Bypassed')");
            });
        builder.Property(row => row.Reason).HasColumnName("reason").HasMaxLength(32);
        builder.Property(row => row.CheckedRevisionNumber).HasColumnName("checked_revision_number");
        builder.Property(row => row.Recommendation).HasColumnName("recommendation").HasConversion<string>().HasMaxLength(32);
        builder.Property(row => row.InputFingerprint).HasColumnName("input_fingerprint").HasMaxLength(64);
        builder.Property(row => row.SourceRunId).HasColumnName("source_run_id").HasMaxLength(32);
        builder.HasOne<BriefAnalysisRunRow>().WithMany().HasForeignKey(row => row.SourceRunId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<BriefRevisionRow>().WithMany()
            .HasForeignKey(row => new { row.RecordId, row.CheckedRevisionNumber }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<BriefAnalysisRow>().WithMany()
            .HasForeignKey(row => new { row.RecordId, row.Task }).OnDelete(DeleteBehavior.Restrict);
    }
}
