using System.Text.RegularExpressions;

namespace CivicLens.Application.Publication;

/// <summary>
/// The one owner of the plain meaning of a latest action text of Congress.gov. Each rule matches an anchored start of the text, in order.
/// No rule matches by a word inside the text. Any other text keeps its own words, so nothing is hidden or guessed.
/// A meaning never calls a bill a success or a failure.
/// </summary>
public static partial class BillStatusMeaning
{
    public const string UnmatchedPrefix = "Latest official action: ";

    public static string Of(string text, string billType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        ArgumentException.ThrowIfNullOrWhiteSpace(billType);
        return Subcommittee(text) ?? Committee(text) ?? Chamber(text, billType) ?? Calendar(text) ?? Reported(text) ?? Law(text)
            ?? UnmatchedPrefix + text;
    }

    private static string? Subcommittee(string text) =>
        SubcommitteeText().Match(text) is { Success: true } match ? $"In committee: sent to the Subcommittee on {match.Groups["name"].Value}." : null;

    private static string? Committee(string text) =>
        CommitteeText().Match(text) is { Success: true } match
            ? $"In committee: sent to the {(match.Groups["chamber"].Success ? match.Groups["chamber"].Value + " " : "")}Committee on {match.Groups["name"].Value}."
            : null;

    private static string? Chamber(string text, string billType)
    {
        var fromHouse = billType.StartsWith('h');
        if (fromHouse && text.StartsWith("Received in the Senate", StringComparison.Ordinal)) return "Passed the House. Now in the Senate.";
        if (!fromHouse && text.StartsWith("Received in the House", StringComparison.Ordinal)) return "Passed the Senate. Now in the House.";
        return null;
    }

    private static string? Calendar(string text)
    {
        if (text.StartsWith("Placed on Senate Legislative Calendar", StringComparison.Ordinal)) return "Waiting for a possible Senate floor vote.";
        if (text.StartsWith("Placed on the Union Calendar", StringComparison.Ordinal) || text.StartsWith("Placed on the House Calendar", StringComparison.Ordinal))
            return "Waiting for a possible House floor vote.";
        return null;
    }

    private static string? Reported(string text) =>
        text.StartsWith("Ordered to be Reported", StringComparison.Ordinal) ? "A committee voted to send this bill to the full chamber." : null;

    private static string? Law(string text) =>
        LawText().Match(text) is { Success: true } match ? $"Became law: Public Law {match.Groups["number"].Value}." : null;

    [GeneratedRegex(@"^Referred to the Subcommittee on (?<name>.+?)\.?$", RegexOptions.ExplicitCapture | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex SubcommitteeText();

    [GeneratedRegex(@"^(?:Read twice and referred|Referred) to the (?:(?<chamber>House|Senate) )?Committee on (?<name>.+?)\.?$", RegexOptions.ExplicitCapture | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex CommitteeText();

    [GeneratedRegex(@"^Became Public Law No: (?<number>\d+-\d+)\.?$", RegexOptions.ExplicitCapture | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex LawText();
}
