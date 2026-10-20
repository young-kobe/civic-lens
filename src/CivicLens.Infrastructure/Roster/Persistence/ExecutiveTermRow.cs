namespace CivicLens.Infrastructure.Roster.Persistence;

internal sealed class ExecutiveTermRow
{
    public string AttemptId { get; set; } = null!;
    public int GovtrackId { get; set; }
    public int TermIndex { get; set; }
    public string Office { get; set; } = null!;
    public DateOnly StartsOn { get; set; }
    public DateOnly EndsOn { get; set; }
    public string Party { get; set; } = null!;
}
