using System.Collections.Immutable;
using CivicLens.Application.Actions;
using CivicLens.Application.Analysis;
using CivicLens.Core.Actions;
using CivicLens.Core.Analysis;
using CivicLens.Core.Documents;
using CivicLens.Core.ExecutiveActions;
using CivicLens.Core.Review;
using static CivicLens.Application.Analysis.AnalysisAnswerRules;

namespace CivicLens.Application.ExecutiveActions;

/// <summary>Turns a model answer into a summary revision, or into the reasons that the repair pass sends back. Every quote must be an exact
/// span of the stored text. Each dollar amount and year of a sentence is in the text that was sent (<see cref="BillFigureSet"/>). The sentences
/// read at the grade limit as one passage, and none holds a phrase of <see cref="AiTextPhrases"/>.
/// It follows <see cref="ActionDraftResolver.ResolveBillText"/>.</summary>
public static class ExecutiveSummaryResolver
{
    public static AnalysisResolution<ExecutiveActionRevision> Resolve(string outputJson, ExecutiveSummaryEvidence evidence, DateTimeOffset createdAtUtc)
    {
        var output = ExecutiveSummaryOutput.Parse(outputJson);
        if (output is null)
            return AnalysisResolution<ExecutiveActionRevision>.Reject(["The output is not valid JSON for the executive summary schema."], false);
        var errors = new List<string>();
        CheckConfidence(output.Summary.Confidence, "summary confidence", errors);
        var items = output.Summary.Sentences;
        if (items.Length < QuotedSentenceLimits.MinimumSentences || items.Length > QuotedSentenceLimits.MaximumSentences)
            errors.Add($"The summary has {items.Length} sentences. Write {QuotedSentenceLimits.MinimumSentences} to {QuotedSentenceLimits.MaximumSentences}.");
        var sentences = items.Take(QuotedSentenceLimits.MaximumSentences).Select(item => Quoted(item, evidence.Text, errors)).OfType<ExecutiveActionSummarySentence>().ToImmutableArray();
        CheckFigures(sentences, new BillFigureSet(evidence.Text.Body), errors);
        errors.AddRange(AiTextPhrases.Errors(sentences.Select(sentence => sentence.Sentence)));
        if (AiTextRules.QuotedPassageGradeError(sentences.Select(sentence => sentence.Sentence)) is { } passageGrade) errors.Add(passageGrade);
        if (errors.Count > 0) return AnalysisResolution<ExecutiveActionRevision>.Reject(Bound(errors), false);
        var summary = new ExecutiveActionSummary(ReviewTextOrigin.Model, evidence.Text.Cut, sentences);
        var revision = ExecutiveActionRevisions.AddSummary(evidence.Current, createdAtUtc, summary);
        revision.ValidateShape();
        return AnalysisResolution<ExecutiveActionRevision>.Accept(revision);
    }

    private static ExecutiveActionSummarySentence? Quoted(AnalysisOutputSentence? item, ExecutiveSummaryText text, List<string> errors)
    {
        var sentence = item?.Sentence?.Trim() ?? string.Empty;
        var quote = item?.Quote ?? string.Empty;
        if (sentence.Length == 0 || sentence.Length > QuotedSentenceLimits.MaximumSentenceLength)
            errors.Add($"A summary sentence has {sentence.Length} characters. Write 1 to {QuotedSentenceLimits.MaximumSentenceLength}.");
        else if (quote.Length > QuotedSentenceLimits.MaximumQuoteLength)
            errors.Add($"A summary quote has {quote.Length} characters. Quote at most {QuotedSentenceLimits.MaximumQuoteLength}.");
        else if (ActionTextCitation.WordCount(quote) < QuotedSentenceLimits.MinimumQuoteWords)
            errors.Add($"The summary quote \"{Shorten(quote)}\" has fewer than {QuotedSentenceLimits.MinimumQuoteWords} words. Quote at least {QuotedSentenceLimits.MinimumQuoteWords}.");
        else if (FindSpan(text, quote) is not { } span)
            errors.Add($"The summary quote \"{Shorten(quote)}\" is not an exact quote of one line of the text that was sent. Copy it exactly.");
        else return new(sentence, span);
        return null;
    }

    /// <summary>The span of an exact quote in the body of the stored text. A quote with a line break or a split surrogate pair has no span.</summary>
    public static SourceSpan? FindSpan(ExecutiveSummaryText text, string quote)
    {
        if (quote.Length == 0 || quote.Contains('\n') || char.IsLowSurrogate(quote[0]) || char.IsHighSurrogate(quote[^1])) return null;
        var offset = text.Body.IndexOf(quote, StringComparison.Ordinal);
        return offset < 0 ? null : new(text.ExtractionId, text.BodyStart + offset, quote.Length);
    }

    /// <summary>A presidential document has no bill number, so no citation token is skipped.</summary>
    private static void CheckFigures(ImmutableArray<ExecutiveActionSummarySentence> sentences, BillFigureSet figures, List<string> errors)
    {
        foreach (var sentence in sentences)
            errors.AddRange(figures.Errors(sentence.Sentence, "summary", "the document text", billNumber: null));
    }
}
