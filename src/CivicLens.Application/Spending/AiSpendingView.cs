namespace CivicLens.Application.Spending;

/// <summary>What the owner sees: the month so far, the cap, the current price of each model, and the latest changes.</summary>
public sealed record AiSpendingView(DateOnly Month, long ChargedMicros, IReadOnlyList<MonthlyCapRevision> Caps,
    IReadOnlyList<ModelPriceRevision> CurrentPrices, IReadOnlyList<ModelPriceRevision> PriceChanges)
{
    public MonthlyCapRevision? CurrentCap => Caps.Count == 0 ? null : Caps[0];
}
