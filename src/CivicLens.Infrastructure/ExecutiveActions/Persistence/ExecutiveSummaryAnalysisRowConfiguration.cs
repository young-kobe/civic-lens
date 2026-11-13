using CivicLens.Infrastructure.Analysis.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.ExecutiveActions.Persistence;

internal sealed class ExecutiveSummaryAnalysisRowConfiguration : IEntityTypeConfiguration<ExecutiveSummaryAnalysisRow>
{
    public void Configure(EntityTypeBuilder<ExecutiveSummaryAnalysisRow> builder)
    {
        AnalysisTableConfiguration.ConfigureQueue<ExecutiveSummaryAnalysisRow, ExecutiveSummaryAnalysisRunRow>(builder, "exec_summary_analysis",
            nameof(ExecutiveSummaryAnalysisRow.RecordId), "record_id");
        builder.HasOne<ExecutiveActionDraftRow>().WithMany().HasForeignKey(row => row.RecordId).OnDelete(DeleteBehavior.Restrict);
    }
}
