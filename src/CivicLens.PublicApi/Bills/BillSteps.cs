using System.Text.RegularExpressions;
using CivicLens.PublicWeb.Components.Ui;
using CivicLens.PublicWeb.Publication;
using CivicLens.Publication.Contracts;

namespace CivicLens.PublicApi.Bills;

/// <summary>
/// The one owner of the steps of the bill page. Each rule reads one whole known shape of an official action text from its start. The step titles
/// are the approved phrases of the Mosaic v2 bill page. A bill that sits in another stage, such as a veto, shows that stage only in its latest
/// action line, because no approved step title exists for it.
/// </summary>
public static partial class BillSteps
{
    private const string ResolvingDifferences = "ResolvingDifferences";

    public static BillStepData[] Of(BillRow bill, string? sponsorName, IReadOnlyList<BillActionRow> actions)
    {
        var chamber = ChamberOf(bill.BillType);
        var introducedBy = sponsorName is null ? "" : ", by " + sponsorName;
        var steps = new List<BillStepData> { new($"Introduced in the {chamber}", Moment.Day(bill.EventDate) + introducedBy) };
        var ordered = actions.OrderBy(action => action.ActionDate).ThenBy(action => action.ActionTime).ThenBy(action => action.Position).ToArray();
        steps.AddRange(Passages(ordered));
        steps.AddRange(AgreedChanges(ordered));
        if (Law(ordered) is { } law) steps.Add(law);
        return [.. steps];
    }

    private static string ChamberOf(string billType) => billType.StartsWith('h') ? "House" : "Senate";

    private static IEnumerable<BillStepData> Passages(BillActionRow[] ordered) => ordered
        .Where(action => action.Type != ResolvingDifferences)
        .Select(action => (action, match: Passage().Match(action.Text)))
        .Where(item => item.match.Success)
        .DistinctBy(item => ChamberGroup(item.match))
        .Select(item => new BillStepData($"Passed the {ChamberGroup(item.match)}", Detail(item.action)));

    private static IEnumerable<BillStepData> AgreedChanges(BillActionRow[] ordered) => ordered
        .Select(action => (action, match: AgreedToChanges().Match(action.Text)))
        .Where(item => item.match.Success && AgreementHolds(item.action.Text))
        .DistinctBy(item => item.match.Groups["chamber"].Value)
        .Select(item => new BillStepData(
            $"{item.match.Groups["chamber"].Value} agreed to the {(item.match.Groups["chamber"].Value == "House" ? "Senate" : "House")} changes", Detail(item.action)));

    /// <summary>The action whose complete text states the public law number, or null.</summary>
    public static BillActionRow? LawAction(IEnumerable<BillActionRow> actions) =>
        actions.FirstOrDefault(action => BillStatusMeaning.PublicLawNumberOf(action.Text) is not null);

    private static BillStepData? Law(BillActionRow[] ordered) => LawAction(ordered) is { } action
        ? new("Became law", $"{Moment.Day(action.ActionDate)}, Public Law {BillStatusMeaning.PublicLawNumberOf(action.Text)}")
        : null;

    private static string ChamberGroup(Match match) => match.Groups["chamber"].Value;

    // A text that says "disagree" or "not agreed" never counts as an agreement.
    private static bool AgreementHolds(string text) =>
        text.Contains("greed to", StringComparison.Ordinal) && !text.Contains("isagree", StringComparison.Ordinal) && !text.Contains("ot agreed", StringComparison.Ordinal);

    private static string Detail(BillActionRow action) =>
        VoteCount().Match(action.Text) is { Success: true } count
            ? $"{Moment.Day(action.ActionDate)}, vote of {count.Groups["yes"].Value} to {count.Groups["no"].Value}"
            : Moment.Day(action.ActionDate);

    [GeneratedRegex(@"^(?:Passed/agreed to in (?<chamber>House|Senate): On (?:passage|motion to suspend the rules and pass)|Passed (?<chamber>House|Senate)\b)",
        RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Passage();

    [GeneratedRegex(@"^Resolving differences -- (?<chamber>House|Senate) actions: .*?(?:agree to|concur in) the (?:Senate|House) amendments?",
        RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex AgreedToChanges();

    [GeneratedRegex(@"(?<![\d,])(?<yes>\d{1,3}) - (?<no>\d{1,3})(?![\d,])", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex VoteCount();
}
