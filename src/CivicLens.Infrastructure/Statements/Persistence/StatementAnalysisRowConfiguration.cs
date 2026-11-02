using CivicLens.Infrastructure.Analysis.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.Statements.Persistence;

internal sealed class StatementAnalysisRowConfiguration : IEntityTypeConfiguration<StatementAnalysisRow>
{
    public void Configure(EntityTypeBuilder<StatementAnalysisRow> builder)
    {
        AnalysisTableConfiguration.ConfigureQueue<StatementAnalysisRow, StatementAnalysisRunRow>(builder, "statement_analysis", nameof(StatementAnalysisRow.CandidateId), "candidate_id");
        builder.HasOne<StatementCandidateRow>().WithMany()
            .HasForeignKey(row => row.CandidateId).OnDelete(DeleteBehavior.Restrict);
    }
}
