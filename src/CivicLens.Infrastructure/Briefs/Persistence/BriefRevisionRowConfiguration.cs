using CivicLens.Core.Briefs;
using CivicLens.Core.Review;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.Briefs.Persistence;

internal sealed class BriefRevisionRowConfiguration : IEntityTypeConfiguration<BriefRevisionRow>
{
    private static readonly string[] WrittenTogether =
    [
        "week_start", "window_start_utc_ticks", "window_end_utc_ticks", "first_release_number", "cutoff_release_number", "fingerprint", "headline",
        "text_origin", "coverage_considered", "coverage_sent_to_model"
    ];

    public void Configure(EntityTypeBuilder<BriefRevisionRow> builder)
    {
        builder.ToTable("brief_revisions", table =>
        {
            table.HasCheckConstraint("ck_brief_revisions_number", "revision_number >= 1");
            table.HasCheckConstraint("ck_brief_revisions_author_kind", "author_kind IN ('Person','Analysis')");
            table.HasCheckConstraint("ck_brief_revisions_run", "(author_kind = 'Analysis') = (run_id IS NOT NULL)");
            table.HasCheckConstraint("ck_brief_revisions_window",
                $"window_end_utc_ticks - window_start_utc_ticks BETWEEN {BriefWindow.ShortestWeekTicks} AND {BriefWindow.LongestWeekTicks} AND " +
                "first_release_number >= 1 AND first_release_number <= cutoff_release_number");
            table.HasCheckConstraint("ck_brief_revisions_fingerprint", "fingerprint ~ '^[0-9a-f]{64}$'");
            table.HasCheckConstraint("ck_brief_revisions_headline", $"length(btrim(headline)) BETWEEN 1 AND {BriefRevision.MaximumHeadlineLength}");
            table.HasCheckConstraint("ck_brief_revisions_text_origin", EnumChecks.InList("text_origin", Enum.GetNames<ReviewTextOrigin>()));
            table.HasCheckConstraint("ck_brief_revisions_person_text", $"author_kind <> '{nameof(ReviewAuthorKind.Person)}' OR text_origin = '{nameof(ReviewTextOrigin.Person)}'");
            table.HasCheckConstraint("ck_brief_revisions_suggestion",
                "(suggestion_headline IS NULL) = (suggestion_run_id IS NULL) AND (suggestion_headline IS NULL) = (suggestion_fingerprint IS NULL) AND " +
                $"length(btrim(suggestion_headline)) BETWEEN 1 AND {BriefRevision.MaximumHeadlineLength} AND " +
                "suggestion_run_id ~ '^[0-9a-f]{32}$' AND suggestion_fingerprint ~ '^[0-9a-f]{64}$'");
            table.HasCheckConstraint("ck_brief_revisions_coverage", "coverage_sent_to_model >= 0 AND coverage_sent_to_model <= coverage_considered");
            table.HasCheckConstraint("ck_brief_revisions_columns_written", $"num_nulls({string.Join(", ", WrittenTogether)}) IN (0, {WrittenTogether.Length})");
        });
        builder.HasKey(row => new { row.RecordId, row.RevisionNumber });
        builder.Property(row => row.RecordId).HasColumnName("record_id").HasMaxLength(64);
        builder.Property(row => row.RevisionNumber).HasColumnName("revision_number");
        builder.Property(row => row.AuthorSubject).HasColumnName("author_subject").HasMaxLength(256).IsRequired();
        builder.Property(row => row.AuthorKind).HasColumnName("author_kind").HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(row => row.RunId).HasColumnName("run_id").HasMaxLength(32);
        builder.Property(row => row.RevisionJson).HasColumnName("revision_json").IsRequired();
        builder.Property(row => row.CreatedAtUtcTicks).HasColumnName("created_at_utc_ticks");
        builder.Property(row => row.WeekStart).HasColumnName("week_start");
        builder.Property(row => row.WindowStartUtcTicks).HasColumnName("window_start_utc_ticks");
        builder.Property(row => row.WindowEndUtcTicks).HasColumnName("window_end_utc_ticks");
        builder.Property(row => row.FirstReleaseNumber).HasColumnName("first_release_number");
        builder.Property(row => row.CutoffReleaseNumber).HasColumnName("cutoff_release_number");
        builder.Property(row => row.Fingerprint).HasColumnName("fingerprint").HasMaxLength(64);
        builder.Property(row => row.Headline).HasColumnName("headline").HasMaxLength(BriefRevision.MaximumHeadlineLength);
        builder.Property(row => row.TextOrigin).HasColumnName("text_origin").HasConversion<string>().HasMaxLength(16);
        builder.Property(row => row.SuggestionHeadline).HasColumnName("suggestion_headline").HasMaxLength(BriefRevision.MaximumHeadlineLength);
        builder.Property(row => row.SuggestionRunId).HasColumnName("suggestion_run_id").HasMaxLength(32);
        builder.Property(row => row.SuggestionFingerprint).HasColumnName("suggestion_fingerprint").HasMaxLength(64);
        builder.Property(row => row.CoverageConsidered).HasColumnName("coverage_considered");
        builder.Property(row => row.CoverageSentToModel).HasColumnName("coverage_sent_to_model");
        builder.HasIndex(row => row.RunId).IsUnique();
        builder.HasOne<BriefDraftRow>().WithMany().HasForeignKey(row => row.RecordId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<BriefDraftRow>().WithMany().HasForeignKey(row => new { row.RecordId, row.WeekStart })
            .HasPrincipalKey(draft => new { draft.RecordId, draft.WeekStart }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<BriefAnalysisRunRow>().WithMany().HasForeignKey(row => row.RunId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<BriefAnalysisRunRow>().WithMany().HasForeignKey(row => row.SuggestionRunId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(row => row.Citations).WithOne().HasForeignKey(citation => new { citation.RecordId, citation.RevisionNumber }).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(row => row.Paragraphs).WithOne().HasForeignKey(paragraph => new { paragraph.RecordId, paragraph.RevisionNumber }).OnDelete(DeleteBehavior.Restrict);
    }
}
