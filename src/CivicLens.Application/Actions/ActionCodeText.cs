using System.Globalization;
using CivicLens.Core.Actions;
using CivicLens.Core.Review;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Actions;

/// <summary>The text that code writes for a bill. The headline names the bill by its number and title, so a list never names a bill by its sponsor.</summary>
public static class ActionCodeText
{
    public const string NoSummarySentence = "Congress.gov has no summary of this bill yet.";

    private const string EndMarks = ".?!";

    public static ReviewText Of(ActionBillSnapshot snapshot)
    {
        var bill = snapshot.Bill;
        var label = $"{BillNumbers.TypeLabel(bill.Type)} {bill.Number}";
        var names = snapshot.Sponsorships.Where(item => item.Role == ActionSponsorshipRole.Sponsor)
            .Select(item => snapshot.OfficialNames[item.PersonId]).Distinct(StringComparer.Ordinal).ToArray();
        var sponsors = string.Join(", ", names);
        var headline = PublicationProtocol.CutAtWord($"{label}: {bill.Title.Trim()}", ActionDraftRevision.MaximumHeadlineLength);
        var sponsorSentence = names.Length == 0 ? "" : $" {(names.Length == 1 ? "Sponsor" : "Sponsors")}: {sponsors}.";
        var introduced = bill.IntroducedDate.ToString("d MMM yyyy", CultureInfo.InvariantCulture);
        var noSummary = bill.SummaryCount == 0 ? " " + NoSummarySentence : "";
        var summary = $"{label}, {EndedSentence(bill.Title)}{sponsorSentence} Introduced {introduced}.{noSummary}";
        return new(headline, summary, ReviewTextOrigin.Code, null);
    }

    private static string EndedSentence(string title)
    {
        var trimmed = title.Trim();
        return EndMarks.Contains(trimmed[^1]) ? trimmed : trimmed + ".";
    }
}
