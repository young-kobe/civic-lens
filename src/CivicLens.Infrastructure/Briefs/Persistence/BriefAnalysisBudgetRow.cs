using CivicLens.Infrastructure.Analysis.Persistence;

namespace CivicLens.Infrastructure.Briefs.Persistence;

internal sealed class BriefAnalysisBudgetRow : IAnalysisBudgetRow
{
    public DateOnly DayUtc { get; set; }
    public string Task { get; set; } = string.Empty;
    public long ChargedTokens { get; set; }
}
