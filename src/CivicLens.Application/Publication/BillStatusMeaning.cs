using System.Text.RegularExpressions;

namespace CivicLens.Application.Publication;

/// <summary>
/// The one owner of the plain meaning of a latest action text of Congress.gov. Each rule matches a whole known shape of the text from its start, in order.
/// A rule never matches by a word inside the text, and a text that has any other ending keeps its own words, so nothing is hidden or guessed.
/// A meaning never calls a bill a success or a failure.
/// </summary>
public static partial class BillStatusMeaning
{
    public const string UnmatchedPrefix = "Latest official action: ";

    private const string Name = @"(?:(?!, and in addition to)[^.(])+?";
    private const string Citation = @"(?:\.|\. \((?:text|consideration|Sponsor introductory remarks on measure): [^\n]*\))?";
    private const string SpeakerTail = ", for a period to be subsequently determined by the Speaker, in each case for consideration of such provisions as fall within the jurisdiction of the committee concerned";
    private const string ChamberReferral = @"(?: and (?:Read twice and )?referred to the .+| ?\. Read twice\. Placed on .+|\.)?";

    public static string Of(string text, string billType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        ArgumentException.ThrowIfNullOrWhiteSpace(billType);
        return Subcommittee(text) ?? Committee(text) ?? Chamber(text, billType) ?? Calendar(text) ?? Reported(text, billType) ?? Law(text)
            ?? UnmatchedPrefix + (text.EndsWith('.') ? text : text + ".");
    }

    private static string? Subcommittee(string text) =>
        SubcommitteeText().Match(text) is { Success: true } match ? $"In committee: sent to the Subcommittee on {match.Groups["name"].Value}." : null;

    private static string? Committee(string text)
    {
        if (CommitteeText().Match(text) is not { Success: true } match) return null;
        var chamber = match.Groups["chamber"].Success ? match.Groups["chamber"].Value + " " : "";
        var addition = match.Groups["more"].Success ? $", and in addition to the {match.Groups["plural"].Value} on {match.Groups["more"].Value}" : "";
        return $"In committee: sent to the {chamber}Committee on {match.Groups["name"].Value}{addition}.";
    }

    private static string? Chamber(string text, string billType)
    {
        var fromHouse = billType.StartsWith('h');
        if (fromHouse && ReceivedInSenate().IsMatch(text)) return "Passed the House. Now in the Senate.";
        if (!fromHouse && ReceivedInHouse().IsMatch(text)) return "Passed the Senate. Now in the House.";
        return null;
    }

    private static string? Calendar(string text)
    {
        if (text.StartsWith("Placed on Senate Legislative Calendar", StringComparison.Ordinal)) return "Waiting for a possible Senate floor vote.";
        if (text.StartsWith("Placed on the Union Calendar", StringComparison.Ordinal) || text.StartsWith("Placed on the House Calendar", StringComparison.Ordinal))
            return "Waiting for a possible House floor vote.";
        return null;
    }

    private static string? Reported(string text, string billType) =>
        OrderedToBeReported().IsMatch(text) ? $"A committee voted to send this {Noun(billType)} to the full chamber." : null;

    private static string? Law(string text) =>
        LawText().Match(text) is { Success: true } match ? $"Became law: Public Law {match.Groups["number"].Value}." : null;

    private static string Noun(string billType) => billType is "hr" or "s" ? "bill" : "measure";

    [GeneratedRegex(@"^Referred to the Subcommittee on (?<name>" + Name + ")" + Citation + "$", RegexOptions.ExplicitCapture | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex SubcommitteeText();

    [GeneratedRegex(@"^(?:Read twice and referred|Referred) to the (?:(?<chamber>House|Senate) )?Committee on (?<name>" + Name + ")"
        + "(?:, and in addition to the (?<plural>Committees?) on (?<more>[^.(]+?)" + SpeakerTail + ")?" + Citation + "$",
        RegexOptions.ExplicitCapture | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex CommitteeText();

    [GeneratedRegex("^Received in the Senate" + ChamberReferral + "$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ReceivedInSenate();

    [GeneratedRegex("^Received in the House" + ChamberReferral + "$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ReceivedInHouse();

    [GeneratedRegex(@"^Ordered to be Reported(?: in the Nature of a Substitute)?(?: \(Amended\))?(?: by (?:Voice Vote|Unanimous Consent|the Yeas and Nays: \d+ - \d+))?\.?$",
        RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex OrderedToBeReported();

    [GeneratedRegex(@"^Became Public Law No: (?<number>\d+-\d+)\.?$", RegexOptions.ExplicitCapture | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex LawText();
}
