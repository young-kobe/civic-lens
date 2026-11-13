namespace CivicLens.Publication.Contracts;

/// <summary>
/// The summary that an AI model wrote from the body text of an executive action: 2 or 3 sentences, each with the exact quote of the text that supports it.
/// <see cref="Cut"/> is true when the model read only the first window of the document. Every citation names the one extraction of the body text.
/// </summary>
public sealed record PublishedExecutiveSummary
{
    public const int MinimumSentences = 2;
    public const int MaximumSentences = 3;

    public required PublishedTextOrigin TextOrigin { get; init; }
    public required bool Cut { get; init; }
    public required PublishedExecutiveSentence[] Sentences { get; init; }

    public IEnumerable<PublishedCitation> Citations() => Sentences.Select(sentence => sentence.Citation);

    public void Validate()
    {
        if (TextOrigin != PublishedTextOrigin.Model)
            throw new InvalidDataException("A published executive action summary is model text.");
        if (Sentences is null || Sentences.Length < MinimumSentences || Sentences.Length > MaximumSentences ||
            Sentences.Any(sentence => sentence?.Citation is null || string.IsNullOrWhiteSpace(sentence.Sentence)))
            throw new InvalidDataException($"A published executive action summary has {MinimumSentences} or {MaximumSentences} sentences that each hold a citation.");
        if (Citations().Select(citation => citation.ExtractionId).Distinct(StringComparer.Ordinal).Count() != 1)
            throw new InvalidDataException("The citations of a published executive action summary name one extraction.");
    }
}

/// <summary>One plain sentence of the summary. The citation holds the exact quote of the document text that supports it.</summary>
public sealed record PublishedExecutiveSentence
{
    public required string Sentence { get; init; }
    public required PublishedCitation Citation { get; init; }
}
