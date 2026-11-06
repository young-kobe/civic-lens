namespace CivicLens.Publication.Contracts;

/// <summary>One plain sentence of a bill text summary. The citation holds the exact quote of the bill text that supports the sentence.</summary>
public sealed record PublishedTextSentence
{
    public required PublishedTextPart Part { get; init; }
    public required string Sentence { get; init; }
    public required PublishedCitation Citation { get; init; }
}
