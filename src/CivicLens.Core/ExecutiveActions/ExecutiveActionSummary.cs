using System.Collections.Immutable;
using CivicLens.Core.Actions;
using CivicLens.Core.Analysis;
using CivicLens.Core.Documents;
using CivicLens.Core.Review;

namespace CivicLens.Core.ExecutiveActions;

/// <summary>The plain summary of the body text of an executive action: 2 or 3 sentences, each with the span of the exact quote that supports it.
/// Every span lies in one extraction, the body text of the original document. <see cref="Cut"/> is true when that extraction keeps only
/// the first window of the document, so the page can say that the summary read the start only. <see cref="QuotedSentenceLimits"/> owns the sentence
/// and quote limits.</summary>
public sealed record ExecutiveActionSummary(ReviewTextOrigin TextOrigin, bool Cut, ImmutableArray<ExecutiveActionSummarySentence> Sentences)
{
    public IEnumerable<SourceSpan> Spans => Sentences.Select(sentence => sentence.Quote);

    public void ValidateShape()
    {
        if (TextOrigin != ReviewTextOrigin.Model)
            throw new ArgumentException("An executive action summary is model text. A person edits no summary yet.");
        if (Sentences.IsDefault || Sentences.Length < QuotedSentenceLimits.MinimumSentences || Sentences.Length > QuotedSentenceLimits.MaximumSentences)
            throw new ArgumentException($"An executive action summary has {QuotedSentenceLimits.MinimumSentences} to {QuotedSentenceLimits.MaximumSentences} sentences.");
        if (Sentences.Any(sentence => string.IsNullOrWhiteSpace(sentence.Sentence) ||
                sentence.Sentence.Length > QuotedSentenceLimits.MaximumSentenceLength))
            throw new ArgumentException($"Each summary sentence has 1 to {QuotedSentenceLimits.MaximumSentenceLength} characters.");
        if (Sentences.Any(sentence => sentence.Quote is not { Start: >= 0, Length: > 0 } quote || !LowercaseHex.Is(quote.ExtractionId, 64) ||
                quote.Length > QuotedSentenceLimits.MaximumQuoteLength))
            throw new ArgumentException("Each summary sentence has a bounded quote span.");
        if (Sentences.Select(sentence => sentence.Quote.ExtractionId).Distinct(StringComparer.Ordinal).Count() != 1)
            throw new ArgumentException("The quotes of a summary lie in one extraction.");
    }

    /// <summary>Each quote is a span of the body of the text extraction. It has at least the least number of words and holds no line break.</summary>
    public void ValidateAgainst(IReadOnlyDictionary<string, DocumentExtraction> extractions)
    {
        var extractionId = Sentences[0].Quote.ExtractionId;
        var extraction = extractions.GetValueOrDefault(extractionId) ?? throw new ArgumentException("A summary quote names a missing extraction.");
        var bodyStart = BillTextBody.Start(extraction.Text);
        foreach (var sentence in Sentences)
        {
            var quote = new DocumentTextSpan(extraction, sentence.Quote.Start, sentence.Quote.Length);
            if (bodyStart < 0 || quote.Start < bodyStart)
                throw new ArgumentException("Each summary quote lies in the body of the document text.");
            if (quote.Quote.Contains('\n') || ActionTextCitation.WordCount(quote.Quote) < QuotedSentenceLimits.MinimumQuoteWords)
                throw new ArgumentException($"Each summary quote has at least {QuotedSentenceLimits.MinimumQuoteWords} words on one line.");
        }
    }
}

/// <summary>One plain sentence of the summary. The quote is the span of the exact words that support it.</summary>
public sealed record ExecutiveActionSummarySentence(string Sentence, SourceSpan Quote);
