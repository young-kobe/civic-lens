using CivicLens.Infrastructure.Analysis.Persistence;
using CivicLens.Infrastructure.Collection.Votes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.Votes.Persistence;

internal sealed class VoteAnalysisRowConfiguration : IEntityTypeConfiguration<VoteAnalysisRow>
{
    public void Configure(EntityTypeBuilder<VoteAnalysisRow> builder)
    {
        AnalysisTableConfiguration.ConfigureQueue<VoteAnalysisRow, VoteAnalysisRunRow>(builder, "vote_analysis", nameof(VoteAnalysisRow.CandidateId), "candidate_id");
        builder.HasOne<VoteCandidateRow>().WithMany()
            .HasForeignKey(row => row.CandidateId).OnDelete(DeleteBehavior.Restrict);
    }
}
