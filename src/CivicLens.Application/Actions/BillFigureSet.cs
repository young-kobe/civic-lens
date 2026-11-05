namespace CivicLens.Application.Actions;

/// <summary>The dollar amounts and years of one bill text. <see cref="Missing"/> lists the figures of a claim that the text lacks. Amounts compare as
/// full numbers, so "$50 million" in a claim matches "$50,000,000" in the text. <see cref="BillFigures"/> owns the patterns.</summary>
public sealed class BillFigureSet
{
    private readonly HashSet<decimal> amounts;
    private readonly HashSet<string> years;

    public BillFigureSet(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        amounts = [.. BillFigures.Amounts(text).Select(amount => amount.Value)];
        years = [.. BillFigures.Years(text)];
    }

    /// <summary>The amounts and years of the claim that the bill text lacks, as the claim writes them. The citation of the bill, such as "H.R. 2025",
    /// is no year. Only that token is skipped, so a year equal to <paramref name="billNumber"/> elsewhere must still be in the text.</summary>
    public IReadOnlyList<string> Missing(string claim, string billNumber)
    {
        ArgumentNullException.ThrowIfNull(claim);
        var claimYears = BillFigures.Years(BillFigures.WithoutBillCitation(claim, billNumber));
        return
        [
            .. BillFigures.Amounts(claim).Where(amount => !amounts.Contains(amount.Value)).Select(amount => amount.Written),
            .. claimYears.Where(year => !years.Contains(year))
        ];
    }
}
