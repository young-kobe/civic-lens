namespace CivicLens.Infrastructure.Analysis.Persistence;

internal sealed class AnalysisOutputSchemaRow : IAnalysisTextRow
{
    public string TextSha256 { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public long CreatedAtUtcTicks { get; set; }
}
