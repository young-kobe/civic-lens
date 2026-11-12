namespace CivicLens.Publication.Contracts;

/// <summary>One key term of a bill. The citation holds the exact quote of the definitions section of the bill text that defines the term.
/// The term and the choice of the term are AI work. The definition is the quote, never AI wording.</summary>
public sealed record PublishedKeyTerm
{
    public const int MaximumTerms = 5;
    public const int MaximumTermLength = 80;

    public required string Term { get; init; }
    public required PublishedCitation Citation { get; init; }
}
