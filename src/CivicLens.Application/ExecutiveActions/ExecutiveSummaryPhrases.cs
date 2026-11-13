using System.Text.RegularExpressions;

namespace CivicLens.Application.ExecutiveActions;

/// <summary>The closed list of phrases that state the aim of a document instead of what it does. The prompt asks the model to write what the
/// document orders or declares. Code finds only these phrases, as whole words with case ignored, and the repair pass sends the one it found back.
/// Code does not judge meaning. The measure report counts the answers that held one.</summary>
public static partial class ExecutiveSummaryPhrases
{
    public static readonly IReadOnlyList<string> Closed = ["aims to", "seeks to", "intended to"];

    [GeneratedRegex(@"\b(aims|seeks|intended)\s+to\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();

    /// <summary>The reason that the repair pass sends back. <see cref="IsReason"/> finds it again, so the measure counts the answers that held a phrase.</summary>
    public static string Reason(string sentence, string phrase) =>
        $"The sentence \"{sentence}\" uses the phrase \"{phrase}\". Write what the document does, and not what it aims at.";

    public static bool IsReason(string error) => error.Contains("\" uses the phrase \"", StringComparison.Ordinal);

    /// <summary>The first listed phrase that the sentence holds, or null.</summary>
    public static string? FindIn(string sentence)
    {
        var match = Pattern().Match(sentence);
        return match.Success ? Closed.Single(phrase => phrase.Split(' ')[0].Equals(match.Groups[1].Value, StringComparison.OrdinalIgnoreCase)) : null;
    }
}
