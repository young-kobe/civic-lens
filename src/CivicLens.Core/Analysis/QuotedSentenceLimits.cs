namespace CivicLens.Core.Analysis;

/// <summary>The one owner of the limits of a quoted summary: model sentences that each carry an exact quote of the source text. The bill text
/// summary of an action, the executive action summary and the quote of an action key term use them.</summary>
public static class QuotedSentenceLimits
{
    public const int MinimumSentences = 2;
    public const int MaximumSentences = 3;
    public const int MaximumSentenceLength = 400;
    public const int MinimumQuoteWords = 4;
    public const int MaximumQuoteLength = 300;
}
