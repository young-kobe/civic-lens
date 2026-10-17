using CivicLens.Application.Votes;
using CivicLens.Infrastructure.Analysis.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.Votes.Persistence;

internal sealed class VoteAnalysisRunRowConfiguration : IEntityTypeConfiguration<VoteAnalysisRunRow>
{
    public void Configure(EntityTypeBuilder<VoteAnalysisRunRow> builder)
    {
        AnalysisTableConfiguration.ConfigureRun(builder, "vote_analysis_runs", nameof(VoteAnalysisRunRow.CandidateId),
            "candidate_id", table =>
            {
                table.HasCheckConstraint("ck_vote_analysis_runs_ids",
                    "run_id ~ '^[0-9a-f]{32}$' AND input_hash ~ '^[0-9a-f]{64}$' AND (previous_run_id IS NULL OR previous_run_id ~ '^[0-9a-f]{32}$')");
                table.HasCheckConstraint("ck_vote_analysis_runs_reason",
                    $"reason IS NULL OR reason IN ('{VoteDraftErrorCodes.Unchanged}','{VoteDraftErrorCodes.NoTrackedMember}','{VoteDraftErrorCodes.MemberVotesChanged}','{VoteDraftErrorCodes.KindChanged}')");
                table.HasCheckConstraint("ck_vote_analysis_runs_outcome",
                    "outcome IN ('Drafted','DraftDiscarded','CitationRejected','OutputRejected','Refused','OutputLimitReached','UnexpectedStop','RateLimited','ProviderUnavailable','AuthenticationFailed','ProviderRejected','ConnectionFailed','BillingFailed','ModelNotFound','ProcessingFailed','InvalidResponse','Interrupted','Bypassed')");
            });
        builder.Property(row => row.Reason).HasColumnName("reason").HasMaxLength(32);
        builder.HasOne<VoteAnalysisRow>().WithMany()
            .HasForeignKey(row => new { row.CandidateId, row.Task }).OnDelete(DeleteBehavior.Restrict);
    }
}
