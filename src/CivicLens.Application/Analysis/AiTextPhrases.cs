using System.Text.RegularExpressions;
using static CivicLens.Application.Analysis.AnalysisAnswerRules;

namespace CivicLens.Application.Analysis;

/// <summary>The one closed list of phrases that public model text never holds: "aims to", "seeks to", "intended to", and the empty phrases.
/// The resolver of each code-approved public text calls <see cref="Errors"/>: the action text from the CRS summary and from the bill text, the vote text, the statement text, and the executive
/// summary. An aim phrase states the aim of a text instead of what it does. An empty phrase says that the text itself, such as "the bill",
/// helps, supports, addresses, or improves something. Code finds only these phrases, as whole words with case ignored, and the repair pass
/// sends back the phrase that it found. Code does not judge meaning. It follows <see cref="AiTextRules"/>.</summary>
public static partial class AiTextPhrases
{
    private const string AimAdvice = "Write what it does, and not what it aims at.";

    private const string EmptyAdvice = "Write who it affects and what it changes.";

    /// <summary>The prompt sentences of the rule. <paramref name="subject"/> names the text in the words of the prompt, such as "the bill".</summary>
    public static string Rule(string subject) =>
        $"Never use the phrases \"aims to\", \"seeks to\", or \"intended to\". Never write that {subject} helps, supports, addresses, or improves " +
        $"something. {EmptyAdvice} Code rejects a sentence with one of these phrases.";

    [GeneratedRegex(@"\b(?:aims|seeks|intended)\s+to\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Aim();

    [GeneratedRegex(@"\b(?:the|this)\s+(?:bill|measure|act|resolution|amendment|motion|order|proclamation|memorandum|notice|document)\s+(?:helps|supports|addresses|improves)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Empty();

    /// <summary>The reason for each sentence of the pieces that holds a phrase. A sentence gives at most one reason.</summary>
    public static IEnumerable<string> Errors(IEnumerable<string?> pieces) =>
        pieces.Select(piece => piece?.Trim() ?? string.Empty).SelectMany(AiTextRules.SentencesOf).Select(ErrorOf).OfType<string>();

    /// <summary>True for a reason of <see cref="Errors"/>, so the measure counts the answers that held a phrase.</summary>
    public static bool IsReason(string error) => error.Contains("\" uses the phrase \"", StringComparison.Ordinal);

    private static string? ErrorOf(string sentence)
    {
        if (Aim().Match(sentence) is { Success: true } aim) return Reason(sentence, aim.Value, AimAdvice);
        return Empty().Match(sentence) is { Success: true } empty ? Reason(sentence, empty.Value, EmptyAdvice) : null;
    }

    private static string Reason(string sentence, string phrase, string advice) =>
        $"The sentence \"{Shorten(sentence)}\" uses the phrase \"{string.Join(' ', phrase.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant()}\". {advice}";
}
