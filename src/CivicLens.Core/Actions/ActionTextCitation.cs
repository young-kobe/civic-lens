using CivicLens.Core.Documents;

namespace CivicLens.Core.Actions;

/// <summary>One plain sentence of an action revision. The span holds the exact quote of the bill text that the sentence contains.</summary>
public sealed record ActionTextCitation(ActionTextPart Part, SourceSpan Span, string Sentence)
{
    private const string SentenceSeparator = " ";

    /// <summary>The one rule that joins sentences into the summary or the why it matters part.</summary>
    public static string Join(IEnumerable<ActionTextCitation> citations) =>
        string.Join(SentenceSeparator, citations.Select(citation => citation.Sentence));

    public static int WordCount(string quote) => quote.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
}

public enum ActionTextPart { Summary, WhyItMatters }
