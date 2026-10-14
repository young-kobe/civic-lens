using CivicLens.Infrastructure.Analysis.Persistence;
using CivicLens.Infrastructure.Collection.BillStream;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.Actions.Persistence;

internal sealed class ActionAnalysisRowConfiguration : IEntityTypeConfiguration<ActionAnalysisRow>
{
    public void Configure(EntityTypeBuilder<ActionAnalysisRow> builder)
    {
        AnalysisTableConfiguration.ConfigureQueue(builder, "action_analysis", nameof(ActionAnalysisRow.CandidateId), "candidate_id");
        builder.HasOne<ActionCandidateRow>().WithMany()
            .HasForeignKey(row => row.CandidateId).OnDelete(DeleteBehavior.Restrict);
    }
}
