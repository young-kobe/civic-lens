using CivicLens.Core.Actions;
using CivicLens.Infrastructure.Collection.BillStream;
using CivicLens.Infrastructure.Documents.Persistence;
using CivicLens.Infrastructure.Review.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.Actions.Persistence;

internal sealed class ActionRevisionRowConfiguration : IEntityTypeConfiguration<ActionRevisionRow>
{
    private static readonly string[] WrittenTogether =
    [
        "headline", "summary", "text_origin", "source_id", "congress", "bill_type", "bill_number", "candidate_id", "candidate_update_utc_ticks",
        "candidate_includes_summaries", "candidate_includes_subjects", "fingerprint", "bill_title", "issue_revision_id", "issue_candidates_recorded"
    ];

    public void Configure(EntityTypeBuilder<ActionRevisionRow> builder)
    {
        builder.ToTable("action_revisions", table =>
        {
            table.HasCheckConstraint("ck_action_revisions_number", "revision_number >= 1");
            table.HasCheckConstraint("ck_action_revisions_author_kind", "author_kind IN ('Person','Analysis','Code')");
            table.HasCheckConstraint("ck_action_revisions_run", "(author_kind <> 'Person') = (run_id IS NOT NULL)");
            table.HasCheckConstraint("ck_action_revisions_person_text", "author_kind <> 'Person' OR text_origin = 'Person'");
            table.HasCheckConstraint("ck_action_revisions_bill", "congress >= 1 AND bill_number >= 1");
            table.HasCheckConstraint("ck_action_revisions_candidate", "candidate_id ~ '^[0-9a-f]{64}$'");
            table.HasCheckConstraint("ck_action_revisions_fingerprint", "fingerprint ~ '^[0-9a-f]{64}$'");
            table.HasCheckConstraint("ck_action_revisions_issue_revision", "issue_revision_id ~ '^[0-9a-f]{64}$'");
            table.HasCheckConstraint("ck_action_revisions_subjects_hash", "subjects_hash ~ '^[0-9a-f]{64}$'");
            table.HasCheckConstraint("ck_action_revisions_summary_version", "(summary_version IS NULL) = (summary_citation_extraction_id IS NULL)");
            table.HasCheckConstraint("ck_action_revisions_crs_summary", "crs_summary_text IS NULL OR (length(btrim(crs_summary_text)) > 0 AND summary_version IS NOT NULL AND summary_citation_extraction_id IS NOT NULL)");
            table.HasCheckConstraint("ck_action_revisions_policy_area", "(policy_area IS NULL) = (policy_area_citation_extraction_id IS NULL)");
            table.HasCheckConstraint("ck_action_revisions_policy_area_id", "policy_area_id IS NULL OR policy_area IS NOT NULL");
            table.HasCheckConstraint("ck_action_revisions_code_bill_citation",
                "author_kind <> 'Code' OR headline IS NULL OR bill_citation_extraction_id IS NOT NULL");
            table.HasCheckConstraint("ck_action_revisions_text_source", "text_package_id IS NULL OR summary_citation_extraction_id IS NULL");
            table.HasCheckConstraint("ck_action_revisions_why_it_matters", "(text_package_id IS NULL) = (why_it_matters IS NULL)");
            table.HasCheckConstraint("ck_action_revisions_cosponsor_count", "cosponsor_count >= 0");
            table.HasCheckConstraint("ck_action_revisions_columns_written", $"num_nulls({string.Join(", ", WrittenTogether)}) IN (0, {WrittenTogether.Length})");
            table.HasCheckConstraint("ck_action_revisions_derived_columns_written", "num_nulls(event_on, cosponsor_count) IN (0, 2)");
            table.HasCheckConstraint("ck_action_revisions_code_version", "code_version ~ '^[0-9a-f]{40}$'");
            table.HasCheckConstraint("ck_action_revisions_code_version_author", "code_version IS NULL OR author_kind = 'Code'");
        });
        builder.HasKey(row => new { row.RecordId, row.RevisionNumber });
        builder.Property(row => row.RecordId).HasColumnName("record_id").HasMaxLength(64);
        builder.Property(row => row.RevisionNumber).HasColumnName("revision_number");
        builder.Property(row => row.AuthorSubject).HasColumnName("author_subject").HasMaxLength(256).IsRequired();
        builder.Property(row => row.AuthorKind).HasColumnName("author_kind").HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(row => row.RunId).HasColumnName("run_id").HasMaxLength(32);
        builder.Property(row => row.CodeVersion).HasColumnName("code_version").HasMaxLength(40);
        builder.Property(row => row.RevisionJson).HasColumnName("revision_json").IsRequired();
        builder.Property(row => row.CreatedAtUtcTicks).HasColumnName("created_at_utc_ticks");
        builder.Property(row => row.SourceId).HasColumnName("source_id").HasMaxLength(ActionDraftRevision.MaximumSourceIdLength);
        builder.Property(row => row.Congress).HasColumnName("congress");
        builder.Property(row => row.BillType).HasColumnName("bill_type").HasMaxLength(ActionDraftRevision.MaximumBillTypeLength);
        builder.Property(row => row.BillNumber).HasColumnName("bill_number");
        builder.Property(row => row.CandidateId).HasColumnName("candidate_id").HasMaxLength(64);
        builder.Property(row => row.CandidateUpdateUtcTicks).HasColumnName("candidate_update_utc_ticks");
        builder.Property(row => row.CandidateIncludesSummaries).HasColumnName("candidate_includes_summaries");
        builder.Property(row => row.CandidateIncludesSubjects).HasColumnName("candidate_includes_subjects");
        builder.Property(row => row.Fingerprint).HasColumnName("fingerprint").HasMaxLength(64);
        builder.Property(row => row.SummaryVersion).HasColumnName("summary_version").HasMaxLength(ActionDraftRevision.MaximumSummaryVersionLength);
        builder.Property(row => row.CrsSummaryText).HasColumnName("crs_summary_text");
        builder.Property(row => row.CrsSummaryRead).HasColumnName("crs_summary_read").HasDefaultValue(false);
        builder.Property(row => row.PolicyArea).HasColumnName("policy_area").HasMaxLength(ActionDraftRevision.MaximumPolicyAreaLength);
        builder.Property(row => row.PolicyAreaId).HasColumnName("policy_area_id").HasMaxLength(ActionDraftRevision.MaximumPolicyAreaIdLength);
        builder.Property(row => row.BillTitle).HasColumnName("bill_title").HasMaxLength(ActionDraftRevision.MaximumTitleLength);
        builder.Property(row => row.IssueRevisionId).HasColumnName("issue_revision_id").HasMaxLength(64);
        builder.Property(row => row.SubjectsHash).HasColumnName("subjects_hash").HasMaxLength(64);
        builder.Property(row => row.TextPackageId).HasColumnName("text_package_id").HasMaxLength(ActionDraftRevision.MaximumTextPackageIdLength);
        builder.Property(row => row.WhyItMatters).HasColumnName("why_it_matters").HasMaxLength(ActionDraftRevision.MaximumSummaryLength);
        builder.Property(row => row.IssueCandidatesRecorded).HasColumnName("issue_candidates_recorded");
        builder.Property(row => row.EventOn).HasColumnName("event_on");
        builder.Property(row => row.CosponsorCount).HasColumnName("cosponsor_count");
        ReviewTextColumns.Configure(builder, "action_revisions", ActionDraftRevision.MaximumHeadlineLength, ActionDraftRevision.MaximumSummaryLength);
        SourceSpanColumns.Configure(builder, "action_revisions", "SummaryCitation", "summary_citation", optional: true);
        SourceSpanColumns.Configure(builder, "action_revisions", "PolicyAreaCitation", "policy_area_citation", optional: true);
        SourceSpanColumns.Configure(builder, "action_revisions", "BillCitation", "bill_citation", optional: true);
        builder.HasIndex(row => row.RunId).IsUnique();
        builder.HasIndex(row => row.EventOn).HasDatabaseName("ix_action_revisions_event_on");
        builder.HasIndex(row => new { row.Congress, row.BillNumber, row.BillType, row.RecordId, row.RevisionNumber })
            .HasDatabaseName("ix_action_revisions_bill_identity")
            .HasFilter("headline IS NOT NULL AND event_on IS NOT NULL");
        builder.HasOne<ActionDraftRow>().WithMany().HasForeignKey(row => row.RecordId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ActionAnalysisRunRow>().WithMany().HasForeignKey(row => row.RunId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ActionAnalysisRunRow>().WithMany().HasForeignKey(row => row.SuggestionRunId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ActionCandidateRow>().WithMany().HasForeignKey(row => row.CandidateId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ActionDraftRow>().WithMany()
            .HasForeignKey(row => new { row.RecordId, row.SourceId, row.Congress, row.BillType, row.BillNumber })
            .HasPrincipalKey(draft => new { draft.RecordId, draft.SourceId, draft.Congress, draft.BillType, draft.BillNumber }).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(row => row.Issues).WithOne().HasForeignKey(issue => new { issue.RecordId, issue.RevisionNumber }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<BillTextVersionRow>().WithMany()
            .HasForeignKey(row => new { row.Congress, row.BillType, row.BillNumber, row.TextPackageId })
            .HasPrincipalKey(text => new { text.Congress, text.BillType, text.BillNumber, text.PackageId }).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(row => row.TextCitations).WithOne().HasForeignKey(citation => new { citation.RecordId, citation.RevisionNumber }).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(row => row.Sponsorships).WithOne().HasForeignKey(item => new { item.RecordId, item.RevisionNumber }).OnDelete(DeleteBehavior.Restrict);
    }
}
