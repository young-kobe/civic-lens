namespace CivicLens.Application.Votes;

/// <summary>Maps the source words of a roll call result to an outcome, as the kind tables map a question to a kind.</summary>
public static class VoteResultTable
{
    private static readonly (string Ending, VoteOutcome Outcome)[] ByEnding =
    [
        ("Passed", VoteOutcome.Passed),
        ("Failed", VoteOutcome.Failed),
        ("Agreed to", VoteOutcome.AgreedTo),
        ("Rejected", VoteOutcome.Rejected),
        ("Confirmed", VoteOutcome.Confirmed)
    ];

    /// <summary>The outcome that the result words end with, or null for any other words. A result that holds the word "not" has no outcome.</summary>
    public static VoteOutcome? OutcomeOf(string result)
    {
        var text = result.Trim();
        if (IsNegated(text)) return null;
        foreach (var (ending, outcome) in ByEnding)
            if (text.Equals(ending, StringComparison.OrdinalIgnoreCase) || text.EndsWith(" " + ending, StringComparison.OrdinalIgnoreCase)) return outcome;
        return null;
    }

    private static bool IsNegated(string text) =>
        text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Any(word => word.Equals("not", StringComparison.OrdinalIgnoreCase));
}
