using CivicLens.Infrastructure.Analysis.Persistence;
using CivicLens.Infrastructure.Collection.HouseVotes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.Votes.Persistence;

internal sealed class VoteAnalysisRowConfiguration : IEntityTypeConfiguration<VoteAnalysisRow>
{
    public void Configure(EntityTypeBuilder<VoteAnalysisRow> builder)
    {
        AnalysisTableConfiguration.ConfigureQueue(builder, "vote_analysis", nameof(VoteAnalysisRow.CandidateId), "candidate_id");
        builder.HasOne<HouseVoteCandidateRow>().WithMany()
            .HasForeignKey(row => row.CandidateId).OnDelete(DeleteBehavior.Restrict);
    }
}
