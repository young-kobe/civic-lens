namespace CivicLens.Infrastructure.Statements.Persistence;

internal sealed class StatementAnalysisBudgetRow
{
    public DateOnly DayUtc { get; set; }
    public string Task { get; set; } = string.Empty;
    public long ChargedTokens { get; set; }
}
