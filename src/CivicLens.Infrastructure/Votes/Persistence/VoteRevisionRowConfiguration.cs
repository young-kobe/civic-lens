using CivicLens.Core.Review;
using CivicLens.Core.Votes;
using CivicLens.Infrastructure.Collection.Votes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.Votes.Persistence;

internal sealed class VoteRevisionRowConfiguration : IEntityTypeConfiguration<VoteRevisionRow>
{
    private static readonly string[] WrittenTogether =
    [
        "headline", "summary", "text_origin", "source_id", "vote_source", "chamber", "congress", "session_number", "roll_call_number", "candidate_id",
        "candidate_update_utc_ticks", "voted_on", "voted_on_citation_extraction_id", "voted_on_citation_start", "voted_on_citation_length", "kind",
        "member_list_id", "issue_revision_id", "issue_candidates_recorded"
    ];

    public void Configure(EntityTypeBuilder<VoteRevisionRow> builder)
    {
        builder.ToTable("vote_revisions", table =>
        {
            table.HasCheckConstraint("ck_vote_revisions_number", "revision_number >= 1");
            table.HasCheckConstraint("ck_vote_revisions_author_kind", "author_kind IN ('Person','Analysis')");
            table.HasCheckConstraint("ck_vote_revisions_run", "(author_kind = 'Analysis') = (run_id IS NOT NULL)");
            table.HasCheckConstraint("ck_vote_revisions_headline", $"length(btrim(headline)) BETWEEN 1 AND {VoteDraftRevision.MaximumHeadlineLength}");
            table.HasCheckConstraint("ck_vote_revisions_summary", $"length(summary) BETWEEN 1 AND {VoteDraftRevision.MaximumSummaryLength}");
            table.HasCheckConstraint("ck_vote_revisions_text_origin", InList("text_origin", Enum.GetNames<ReviewTextOrigin>()));
            table.HasCheckConstraint("ck_vote_revisions_suggestion",
                "(suggestion_headline IS NULL) = (suggestion_summary IS NULL) AND (suggestion_headline IS NULL) = (suggestion_run_id IS NULL) AND " +
                "(suggestion_headline IS NULL) = (suggestion_fingerprint IS NULL)");
            table.HasCheckConstraint("ck_vote_revisions_vote_source", InList("vote_source", Enum.GetNames<VoteSource>()));
            table.HasCheckConstraint("ck_vote_revisions_chamber", "chamber IN ('House', 'Senate')");
            table.HasCheckConstraint("ck_vote_revisions_roll", "congress >= 1 AND session_number IN (1, 2) AND roll_call_number >= 1");
            table.HasCheckConstraint("ck_vote_revisions_chamber_source", "(chamber = 'House') = (vote_source = 'CongressGov')");
            table.HasCheckConstraint("ck_vote_revisions_bill", "(bill_type IS NULL) = (bill_number IS NULL) AND bill_number >= 1");
            table.HasCheckConstraint("ck_vote_revisions_kind", InList("kind", Enum.GetNames<VoteKind>()));
            table.HasCheckConstraint("ck_vote_revisions_issue_revision", "issue_revision_id ~ '^[0-9a-f]{64}$'");
            table.HasCheckConstraint("ck_vote_revisions_subjects_hash", "subjects_hash ~ '^[0-9a-f]{64}$'");
            table.HasCheckConstraint("ck_vote_revisions_columns_written", $"num_nulls({string.Join(", ", WrittenTogether)}) IN (0, {WrittenTogether.Length})");
        });
        builder.HasKey(row => new { row.RecordId, row.RevisionNumber });
        builder.Property(row => row.RecordId).HasColumnName("record_id").HasMaxLength(64);
        builder.Property(row => row.RevisionNumber).HasColumnName("revision_number");
        builder.Property(row => row.AuthorSubject).HasColumnName("author_subject").HasMaxLength(256).IsRequired();
        builder.Property(row => row.AuthorKind).HasColumnName("author_kind").HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(row => row.RunId).HasColumnName("run_id").HasMaxLength(32);
        builder.Property(row => row.RevisionJson).HasColumnName("revision_json").IsRequired();
        builder.Property(row => row.CreatedAtUtcTicks).HasColumnName("created_at_utc_ticks");
        builder.Property(row => row.Headline).HasColumnName("headline").HasMaxLength(VoteDraftRevision.MaximumHeadlineLength);
        builder.Property(row => row.Summary).HasColumnName("summary");
        builder.Property(row => row.TextOrigin).HasColumnName("text_origin").HasConversion<string>().HasMaxLength(16);
        builder.Property(row => row.SuggestionHeadline).HasColumnName("suggestion_headline").HasMaxLength(VoteDraftRevision.MaximumHeadlineLength);
        builder.Property(row => row.SuggestionSummary).HasColumnName("suggestion_summary");
        builder.Property(row => row.SuggestionRunId).HasColumnName("suggestion_run_id").HasMaxLength(32);
        builder.Property(row => row.SuggestionFingerprint).HasColumnName("suggestion_fingerprint").HasMaxLength(64);
        builder.Property(row => row.SourceId).HasColumnName("source_id").HasMaxLength(128);
        builder.Property(row => row.VoteSource).HasColumnName("vote_source").HasConversion<string>().HasMaxLength(16);
        builder.Property(row => row.Chamber).HasColumnName("chamber").HasConversion<string>().HasMaxLength(16);
        builder.Property(row => row.Congress).HasColumnName("congress");
        builder.Property(row => row.SessionNumber).HasColumnName("session_number");
        builder.Property(row => row.RollCallNumber).HasColumnName("roll_call_number");
        builder.Property(row => row.BillType).HasColumnName("bill_type").HasMaxLength(VoteDraftRevision.MaximumBillTypeLength);
        builder.Property(row => row.BillNumber).HasColumnName("bill_number");
        builder.Property(row => row.CandidateId).HasColumnName("candidate_id").HasMaxLength(64);
        builder.Property(row => row.CandidateUpdateUtcTicks).HasColumnName("candidate_update_utc_ticks");
        builder.Property(row => row.VotedOn).HasColumnName("voted_on");
        builder.Property(row => row.Kind).HasColumnName("kind").HasConversion<string>().HasMaxLength(24);
        builder.Property(row => row.MemberListId).HasColumnName("member_list_id").HasMaxLength(64);
        builder.Property(row => row.IssueRevisionId).HasColumnName("issue_revision_id").HasMaxLength(64);
        builder.Property(row => row.SubjectsHash).HasColumnName("subjects_hash").HasMaxLength(64);
        builder.Property(row => row.IssueCandidatesRecorded).HasColumnName("issue_candidates_recorded");
        VoteCitationColumns.Configure(builder, "vote_revisions", "VotedOnCitation", "voted_on_citation", optional: false);
        VoteCitationColumns.Configure(builder, "vote_revisions", "KindCitation", "kind_citation", optional: true);
        builder.HasIndex(row => row.RunId).IsUnique();
        builder.HasOne<VoteDraftRow>().WithMany().HasForeignKey(row => row.RecordId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<VoteAnalysisRunRow>().WithMany().HasForeignKey(row => row.RunId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<VoteAnalysisRunRow>().WithMany().HasForeignKey(row => row.SuggestionRunId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<VoteCandidateRow>().WithMany().HasForeignKey(row => row.CandidateId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<VoteMemberListRow>().WithMany().HasForeignKey(row => row.MemberListId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<VoteDraftRow>().WithMany()
            .HasForeignKey(row => new { row.RecordId, row.Chamber, row.Congress, row.SessionNumber, row.RollCallNumber })
            .HasPrincipalKey(draft => new { draft.RecordId, draft.Chamber, draft.Congress, draft.SessionNumber, draft.RollCallNumber }).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(row => row.Facts).WithOne().HasForeignKey(fact => new { fact.RecordId, fact.RevisionNumber }).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(row => row.Issues).WithOne().HasForeignKey(issue => new { issue.RecordId, issue.RevisionNumber }).OnDelete(DeleteBehavior.Restrict);
    }

    private static string InList(string column, IEnumerable<string> names) => $"{column} IN ({string.Join(", ", names.Select(name => $"'{name}'"))})";
}
