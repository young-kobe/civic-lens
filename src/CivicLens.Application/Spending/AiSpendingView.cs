namespace CivicLens.Application.Spending;

/// <summary>What the owner sees: the month so far, the cap, and the latest cap changes.</summary>
public sealed record AiSpendingView(DateOnly Month, long ChargedMicros, IReadOnlyList<MonthlyCapRevision> Caps)
{
    public MonthlyCapRevision? CurrentCap => Caps.Count == 0 ? null : Caps[0];
}
