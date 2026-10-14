using CivicLens.Infrastructure.Analysis.Persistence;

namespace CivicLens.Infrastructure.Actions.Persistence;

internal sealed class ActionAnalysisBudgetRow : IAnalysisBudgetRow
{
    public DateOnly DayUtc { get; set; }
    public string Task { get; set; } = string.Empty;
    public long ChargedTokens { get; set; }
}
