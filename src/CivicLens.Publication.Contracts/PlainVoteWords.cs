namespace CivicLens.Publication.Contracts;

/// <summary>
/// The one owner of the plain words for a roll call: casts, vote kinds, results, and the format that puts the source words in brackets.
/// The Host reads these words. A page never keeps its own copy.
/// </summary>
public static class PlainVoteWords
{
    public const string Yes = "Yes";
    public const string No = "No";
    public const string Present = "Present";
    public const string NotVoting = "Not voting";
    public const string OtherKind = "Other question";

    private static readonly (string Ending, string Words)[] ResultByEnding =
    [
        ("Not Confirmed", "Not confirmed"),
        ("Not Agreed to", "Failed"),
        ("Nomination Rejected", "Not confirmed"),
        ("Passed", "Passed"),
        ("Agreed to", "Passed"),
        ("Confirmed", "Confirmed"),
        ("Failed", "Failed"),
        ("Rejected", "Failed")
    ];

    /// <summary>The plain word for a source cast, or null for a cast with none, such as a vote for a candidate.</summary>
    public static string? CastWord(string cast) => cast switch
    {
        PublishedOfficialVote.Present => Present,
        PublishedOfficialVote.NotVoting => NotVoting,
        _ => PublishedOfficialVote.SideOf(cast) switch
        {
            PublishedPartyMajority.Yea => Yes,
            PublishedPartyMajority.Nay => No,
            _ => null
        }
    };

    /// <summary>A cast as the page shows it: "Yes (Aye)", "No (Nay)", "Present", "Not voting". A candidate name shows alone.</summary>
    public static string Cast(string cast) => PlainWithSource(CastWord(cast), cast);

    /// <summary>The plain name of a vote kind. It reads right for a bill, a resolution, and a vote to agree to a Senate change.</summary>
    public static string Kind(PublishedVoteKind kind) => kind switch
    {
        PublishedVoteKind.Rule => "Vote to set the rules for debate",
        PublishedVoteKind.PreviousQuestion => "Vote to end debate and vote now",
        PublishedVoteKind.MotionToRecommit => "Vote to send back to committee",
        PublishedVoteKind.Amendment => "Vote to change the text",
        PublishedVoteKind.Suspension => "Vote to pass with limited debate",
        PublishedVoteKind.Passage => "Vote to pass or agree to",
        PublishedVoteKind.ConferenceReport => "Vote to accept the final text from both chambers",
        PublishedVoteKind.VetoOverride => "Vote to override the veto",
        PublishedVoteKind.Nomination => "Vote to confirm",
        PublishedVoteKind.Cloture => "Vote to end debate",
        PublishedVoteKind.MotionToProceed => "Vote to start debate",
        PublishedVoteKind.Other => OtherKind,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Vote kind has no plain name.")
    };

    /// <summary>The question line: the plain kind name, then the source question in brackets. The kind Other has no plain question, so its source words show alone.</summary>
    public static string Question(PublishedVoteKind kind, string source) =>
        PlainWithSource(kind == PublishedVoteKind.Other ? null : Kind(kind), source);

    /// <summary>The plain words for a source result, or null when no row knows it. Words before the ending may not hold the word "not".</summary>
    public static string? ResultWord(string result)
    {
        var text = result.Trim();
        foreach (var (ending, words) in ResultByEnding)
            if (EndsWithWords(text, ending) is { } before && !HasWordNot(before)) return words;
        return null;
    }

    /// <summary>The result line: plain words, then the source result in brackets. An unknown result shows alone.</summary>
    public static string Result(string result) => PlainWithSource(ResultWord(result), result.Trim());

    /// <summary>"Yes 210, No 200, Not voting 5". Present shows only when it is not zero.</summary>
    public static string Totals(PublishedVoteTotals totals) =>
        $"{YesNo(totals.Yea, totals.Nay)}{(totals.Present > 0 ? $", {Present} {totals.Present}" : "")}, {NotVoting} {totals.NotVoting}";

    public static string YesNo(int yea, int nay) => $"{Yes} {yea}, {No} {nay}";

    /// <summary>
    /// The one format: plain words first, source words in brackets. No plain words show the source alone.
    /// Words that match the source ignoring case show once, in the plain spelling.
    /// </summary>
    public static string PlainWithSource(string? plain, string source) =>
        plain is null ? source : plain.Equals(source, StringComparison.OrdinalIgnoreCase) ? plain : $"{plain} ({source})";

    private static string? EndsWithWords(string text, string ending)
    {
        if (text.Equals(ending, StringComparison.OrdinalIgnoreCase)) return "";
        return text.EndsWith(" " + ending, StringComparison.OrdinalIgnoreCase) ? text[..^ending.Length] : null;
    }

    private static bool HasWordNot(string text) =>
        text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Any(word => word.Equals("not", StringComparison.OrdinalIgnoreCase));
}
