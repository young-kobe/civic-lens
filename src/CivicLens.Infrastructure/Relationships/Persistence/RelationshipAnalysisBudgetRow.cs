using CivicLens.Infrastructure.Analysis.Persistence;

namespace CivicLens.Infrastructure.Relationships.Persistence;

internal sealed class RelationshipAnalysisBudgetRow : IAnalysisBudgetRow
{
    public DateOnly DayUtc { get; set; }
    public string Task { get; set; } = string.Empty;
    public long ChargedTokens { get; set; }
}
