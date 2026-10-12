using CivicLens.Infrastructure.Analysis.Persistence;

namespace CivicLens.Infrastructure.Statements.Persistence;

internal sealed class StatementAnalysisBudgetRow : IAnalysisBudgetRow
{
    public DateOnly DayUtc { get; set; }
    public string Task { get; set; } = string.Empty;
    public long ChargedTokens { get; set; }
}
