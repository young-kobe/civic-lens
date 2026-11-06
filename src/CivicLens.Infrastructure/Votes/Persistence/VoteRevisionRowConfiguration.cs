using CivicLens.Core.Votes;
using CivicLens.Infrastructure.Collection.Votes;
using CivicLens.Infrastructure.Documents.Persistence;
using CivicLens.Infrastructure.Review.Persistence;
using CivicLens.Publication.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.Votes.Persistence;

internal sealed class VoteRevisionRowConfiguration : IEntityTypeConfiguration<VoteRevisionRow>
{
    public void Configure(EntityTypeBuilder<VoteRevisionRow> builder)
    {
        builder.ToTable("vote_revisions", table =>
        {
            table.HasCheckConstraint("ck_vote_revisions_number", "revision_number >= 1");
            table.HasCheckConstraint("ck_vote_revisions_author_kind", "author_kind IN ('Person','Analysis')");
            table.HasCheckConstraint("ck_vote_revisions_run", "(author_kind = 'Analysis') = (run_id IS NOT NULL)");
            table.HasCheckConstraint("ck_vote_revisions_vote_source", EnumChecks.InList("vote_source", Enum.GetNames<VoteSource>()));
            table.HasCheckConstraint("ck_vote_revisions_chamber", "chamber IN ('House', 'Senate')");
            table.HasCheckConstraint("ck_vote_revisions_roll", "congress >= 1 AND session_number IN (1, 2) AND roll_call_number >= 1");
            table.HasCheckConstraint("ck_vote_revisions_chamber_source", "(chamber = 'House') = (vote_source = 'CongressGov')");
            table.HasCheckConstraint("ck_vote_revisions_bill", "(bill_type IS NULL) = (bill_number IS NULL) AND bill_number >= 1");
            table.HasCheckConstraint("ck_vote_revisions_kind", EnumChecks.InList("kind", Enum.GetNames<VoteKind>()));
            table.HasCheckConstraint("ck_vote_revisions_issue_revision", "issue_revision_id ~ '^[0-9a-f]{64}$'");
            table.HasCheckConstraint("ck_vote_revisions_subjects_hash", "subjects_hash ~ '^[0-9a-f]{64}$'");
            table.HasCheckConstraint("ck_vote_revisions_alignment", $"alignment IS NULL OR {EnumChecks.InList("alignment", Enum.GetNames<PublishedVoteAlignment>())}");
            table.HasCheckConstraint("ck_vote_revisions_majorities",
                $"(democrat_majority IS NULL OR democrat_majority IN ('{PublishedPartyMajority.Yea}', '{PublishedPartyMajority.Nay}')) AND " +
                $"(republican_majority IS NULL OR republican_majority IN ('{PublishedPartyMajority.Yea}', '{PublishedPartyMajority.Nay}'))");
        });
        builder.HasKey(row => new { row.RecordId, row.RevisionNumber });
        builder.Property(row => row.RecordId).HasColumnName("record_id").HasMaxLength(64);
        builder.Property(row => row.RevisionNumber).HasColumnName("revision_number");
        builder.Property(row => row.AuthorSubject).HasColumnName("author_subject").HasMaxLength(256).IsRequired();
        builder.Property(row => row.AuthorKind).HasColumnName("author_kind").HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(row => row.RunId).HasColumnName("run_id").HasMaxLength(32);
        builder.Property(row => row.CreatedAtUtcTicks).HasColumnName("created_at_utc_ticks");
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
        builder.Property(row => row.Question).HasColumnName("question").HasMaxLength(VoteDraftRevision.MaximumFactLength);
        builder.Property(row => row.Result).HasColumnName("result").HasMaxLength(VoteDraftRevision.MaximumFactLength).IsRequired();
        builder.Property(row => row.Alignment).HasColumnName("alignment").HasConversion<string>().HasMaxLength(16);
        builder.Property(row => row.DemocratMajority).HasColumnName("democrat_majority").HasMaxLength(8);
        builder.Property(row => row.RepublicanMajority).HasColumnName("republican_majority").HasMaxLength(8);
        ReviewTextColumns.Configure(builder, "vote_revisions", VoteDraftRevision.MaximumHeadlineLength, VoteDraftRevision.MaximumSummaryLength);
        builder.Property(row => row.Headline).IsRequired();
        builder.Property(row => row.Summary).IsRequired();
        builder.Property(row => row.TextOrigin).IsRequired();
        SourceSpanColumns.Configure(builder, "vote_revisions", "VotedOnCitation", "voted_on_citation", optional: false);
        SourceSpanColumns.Configure(builder, "vote_revisions", "KindCitation", "kind_citation", optional: true);
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
}
