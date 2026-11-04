namespace CivicLens.Infrastructure.Analysis.Persistence;

internal interface IAnalysisTextRow
{
    string TextSha256 { get; set; }
    string Text { get; set; }
    long CreatedAtUtcTicks { get; set; }
}
