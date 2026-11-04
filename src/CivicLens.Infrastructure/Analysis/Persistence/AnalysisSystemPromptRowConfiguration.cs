using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.Analysis.Persistence;

internal sealed class AnalysisSystemPromptRowConfiguration : IEntityTypeConfiguration<AnalysisSystemPromptRow>
{
    public void Configure(EntityTypeBuilder<AnalysisSystemPromptRow> builder) =>
        AnalysisTableConfiguration.ConfigureText(builder, "analysis_system_prompts");
}
