using System.Globalization;
using CivicLens.Core.Actions;
using CivicLens.Core.Review;

namespace CivicLens.Application.Actions;

public static class ActionCodeText
{
    private static readonly Dictionary<string, string> BillTypeLabels = new(StringComparer.Ordinal)
    {
        ["hr"] = "H.R.",
        ["s"] = "S.",
        ["hjres"] = "H.J.Res.",
        ["sjres"] = "S.J.Res.",
        ["hconres"] = "H.Con.Res.",
        ["sconres"] = "S.Con.Res.",
        ["hres"] = "H.Res.",
        ["sres"] = "S.Res."
    };

    public const string NoSummarySentence = "Congress.gov has no summary of this bill yet.";

    private const string EndMarks = ".?!";

    public static ReviewText Of(ActionBillSnapshot snapshot)
    {
        var bill = snapshot.Bill;
        var label = BillNumber(bill.Type, bill.Number);
        var names = snapshot.Sponsorships.Where(item => item.Role == ActionSponsorshipRole.Sponsor)
            .Select(item => snapshot.OfficialNames[item.PersonId]).Distinct(StringComparer.Ordinal).ToArray();
        var sponsors = string.Join(", ", names);
        var headline = names.Length == 0 ? $"{label}: introduced in Congress" : $"{label}: introduced by {sponsors}";
        var sponsorSentence = names.Length == 0 ? "" : $" {(names.Length == 1 ? "Sponsor" : "Sponsors")}: {sponsors}.";
        var introduced = bill.IntroducedDate.ToString("d MMM yyyy", CultureInfo.InvariantCulture);
        var noSummary = bill.SummaryCount == 0 ? " " + NoSummarySentence : "";
        var summary = $"{label}, {EndedSentence(bill.Title)}{sponsorSentence} Introduced {introduced}.{noSummary}";
        return new(headline, summary, ReviewTextOrigin.Code, null);
    }

    /// <summary>The official display style of a bill number, such as "H.R. 9576", "H.J.Res. 213" or "S. 2403". Display code uses this one owner. A type outside the table, which a published vote can hold, shows in upper case.</summary>
    public static string BillNumber(string billType, int number) =>
        $"{(BillTypeLabels.TryGetValue(billType.ToLowerInvariant(), out var label) ? label : billType.ToUpperInvariant())} {number}";

    private static string EndedSentence(string title)
    {
        var trimmed = title.Trim();
        return EndMarks.Contains(trimmed[^1]) ? trimmed : trimmed + ".";
    }
}
