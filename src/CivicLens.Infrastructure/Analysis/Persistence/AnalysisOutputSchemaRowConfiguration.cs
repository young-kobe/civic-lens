using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.Analysis.Persistence;

internal sealed class AnalysisOutputSchemaRowConfiguration : IEntityTypeConfiguration<AnalysisOutputSchemaRow>
{
    public void Configure(EntityTypeBuilder<AnalysisOutputSchemaRow> builder) =>
        AnalysisTableConfiguration.ConfigureText(builder, "analysis_output_schemas");
}
