using CivicLens.Infrastructure.Analysis.Persistence;

namespace CivicLens.Infrastructure.Votes.Persistence;

internal sealed class VoteAnalysisBudgetRow : IAnalysisBudgetRow
{
    public DateOnly DayUtc { get; set; }
    public string Task { get; set; } = string.Empty;
    public long ChargedTokens { get; set; }
}
