namespace CivicLens.Infrastructure.Analysis.Persistence;

internal sealed class DocumentChangeAnalysisBudgetRow
{
    public DateOnly DayUtc { get; set; }
    public long ChargedTokens { get; set; }
}
