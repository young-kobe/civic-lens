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

    public static ReviewText Of(ActionBillSnapshot snapshot)
    {
        var bill = snapshot.Bill;
        var label = $"{LabelOf(bill.Type)} {bill.Number}";
        var sponsors = string.Join(", ", snapshot.Sponsorships.Where(item => item.Role == ActionSponsorshipRole.Sponsor)
            .Select(item => snapshot.OfficialNames[item.PersonId]).Distinct(StringComparer.Ordinal));
        var headline = sponsors.Length == 0 ? $"{label}: introduced in Congress" : $"{label}: introduced by {sponsors}";
        var sponsorSentence = sponsors.Length == 0 ? "" : $" Sponsor: {sponsors}.";
        var introduced = bill.IntroducedDate.ToString("MMMM d, yyyy", CultureInfo.InvariantCulture);
        var summary = $"{label}, {bill.Title.TrimEnd('.')}.{sponsorSentence} Introduced {introduced}. Congress.gov has no summary of this bill yet.";
        return new(headline, summary, ReviewTextOrigin.Code, null);
    }

    private static string LabelOf(string billType) =>
        BillTypeLabels.GetValueOrDefault(billType.ToLowerInvariant(), billType.ToUpperInvariant());
}
