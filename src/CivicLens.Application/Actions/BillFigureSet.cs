using static CivicLens.Application.Analysis.AnalysisAnswerRules;

namespace CivicLens.Application.Actions;

/// <summary>The dollar amounts and years of one bill text. <see cref="Missing"/> lists the figures of a claim that the text lacks. Amounts compare as
/// full numbers, so "$50 million" in a claim matches "$50,000,000" in the text. <see cref="BillFigures"/> owns the patterns. The bill text resolver and
/// the executive summary resolver both refuse a figure that the sent text lacks, with the one message of <see cref="Errors"/>.</summary>
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
    /// is no year. Only that token is skipped, so a year equal to <paramref name="billNumber"/> elsewhere must still be in the text. A text with no bill
    /// number, such as a presidential document, passes null and skips nothing.</summary>
    public IReadOnlyList<string> Missing(string claim, string? billNumber)
    {
        ArgumentNullException.ThrowIfNull(claim);
        var claimYears = BillFigures.Years(billNumber is null ? claim : BillFigures.WithoutBillCitation(claim, billNumber));
        return
        [
            .. BillFigures.Amounts(claim).Where(amount => !amounts.Contains(amount.Value)).Select(amount => amount.Written),
            .. claimYears.Where(year => !years.Contains(year))
        ];
    }

    /// <summary>The repair reason for each figure of the claim that the text lacks. <paramref name="source"/> names the sent text, such as
    /// "the bill text".</summary>
    public IEnumerable<string> Errors(string claim, string field, string source, string? billNumber) =>
        Missing(claim, billNumber).Select(figure =>
            $"The {field} has the figure \"{Shorten(figure)}\", which is not in {source} that was sent. Use only amounts and years that {source} has.");
}
