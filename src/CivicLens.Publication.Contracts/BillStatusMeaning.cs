using System.Text.RegularExpressions;

namespace CivicLens.Publication.Contracts;

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

    /// <summary>Returns the public law number only when the complete official action has the known public law shape.</summary>
    public static string? PublicLawNumberOf(string officialActionText)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(officialActionText);
        return LawText().Match(officialActionText) is { Success: true } match ? match.Groups["number"].Value : null;
    }

    public static string Of(string text, string billType) => Known(text, billType)?.Meaning ?? UnmatchedPrefix + Sentence(text);

    /// <summary>The meaning of a known text, or the official text as a sentence when no rule matches. It never adds the unmatched prefix.</summary>
    public static string Plain(string text, string billType) => Known(text, billType)?.Meaning ?? Sentence(text);

    /// <summary>The short label of the status, such as "In committee". Each label uses only words of the plain meaning of the same text. A text that no rule matches has no label.</summary>
    public static string? Label(string text, string billType) => Known(text, billType)?.Label;

    private static Status? Known(string text, string billType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        ArgumentException.ThrowIfNullOrWhiteSpace(billType);
        return Subcommittee(text) ?? Committee(text) ?? Chamber(text, billType) ?? Calendar(text) ?? Reported(text, billType) ?? Law(text);
    }

    private static string Sentence(string text) => text.EndsWith('.') ? text : text + ".";

    private static Status? Subcommittee(string text) =>
        SubcommitteeText().Match(text) is { Success: true } match ? new(InCommittee, $"In committee: sent to the Subcommittee on {match.Groups["name"].Value}.") : null;

    private static Status? Committee(string text)
    {
        if (CommitteeText().Match(text) is not { Success: true } match) return null;
        var chamber = match.Groups["chamber"].Success ? match.Groups["chamber"].Value + " " : "";
        var addition = match.Groups["more"].Success ? $", and in addition to the {match.Groups["plural"].Value} on {match.Groups["more"].Value}" : "";
        return new(InCommittee, $"In committee: sent to the {chamber}Committee on {match.Groups["name"].Value}{addition}.");
    }

    private static Status? Chamber(string text, string billType)
    {
        var fromHouse = billType.StartsWith('h');
        if (fromHouse && ReceivedInSenate().IsMatch(text)) return new("Passed the House", "Passed the House. Now in the Senate.");
        if (!fromHouse && ReceivedInHouse().IsMatch(text)) return new("Passed the Senate", "Passed the Senate. Now in the House.");
        return null;
    }

    private static Status? Calendar(string text)
    {
        if (text.StartsWith("Placed on Senate Legislative Calendar", StringComparison.Ordinal))
            return new("Waiting for a possible Senate floor vote", "Waiting for a possible Senate floor vote.");
        if (text.StartsWith("Placed on the Union Calendar", StringComparison.Ordinal) || text.StartsWith("Placed on the House Calendar", StringComparison.Ordinal))
            return new("Waiting for a possible House floor vote", "Waiting for a possible House floor vote.");
        return null;
    }

    private static Status? Reported(string text, string billType) => OrderedToBeReported().IsMatch(text)
        ? new("A committee voted to send this to the full chamber", $"A committee voted to send this {Noun(billType)} to the full chamber.")
        : null;

    private static Status? Law(string text) =>
        PublicLawNumberOf(text) is { } number ? new("Became law", $"Became law: Public Law {number}.") : null;

    private sealed record Status(string Label, string Meaning);

    private const string InCommittee = "In committee";

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

    [GeneratedRegex(@"\ABecame Public Law No: (?<number>[0-9]+-[0-9]+)\.?\z", RegexOptions.ExplicitCapture | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex LawText();
}
