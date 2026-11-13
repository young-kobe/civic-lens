using System.Collections.Immutable;
using CivicLens.Application.Actions;
using CivicLens.Application.Analysis;
using CivicLens.Core.Actions;
using CivicLens.Core.Documents;
using CivicLens.Core.ExecutiveActions;
using CivicLens.Core.Review;
using static CivicLens.Application.Analysis.AnalysisAnswerRules;

namespace CivicLens.Application.ExecutiveActions;

/// <summary>Turns a model answer into a summary revision, or into the reasons that the repair pass sends back. Every quote must be an exact
/// span of the stored text. The sentences read at the grade limit as one passage, and none holds a phrase of <see cref="ExecutiveSummaryPhrases"/>.
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
        if (items.Length < ExecutiveActionSummary.MinimumSentences || items.Length > ExecutiveActionSummary.MaximumSentences)
            errors.Add($"The summary has {items.Length} sentences. Write {ExecutiveActionSummary.MinimumSentences} to {ExecutiveActionSummary.MaximumSentences}.");
        var sentences = items.Take(ExecutiveActionSummary.MaximumSentences).Select(item => Quoted(item, evidence.Text, errors)).OfType<ExecutiveActionSummarySentence>().ToImmutableArray();
        CheckPhrases(sentences, errors);
        CheckGrade(sentences, errors);
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
        if (sentence.Length == 0 || sentence.Length > ActionDraftRevision.MaximumTextSentenceLength)
            errors.Add($"A summary sentence has {sentence.Length} characters. Write 1 to {ActionDraftRevision.MaximumTextSentenceLength}.");
        else if (quote.Length > ActionDraftRevision.MaximumTextQuoteLength)
            errors.Add($"A summary quote has {quote.Length} characters. Quote at most {ActionDraftRevision.MaximumTextQuoteLength}.");
        else if (ActionTextCitation.WordCount(quote) < ActionDraftRevision.MinimumTextQuoteWords)
            errors.Add($"The summary quote \"{Shorten(quote)}\" has fewer than {ActionDraftRevision.MinimumTextQuoteWords} words. Quote at least {ActionDraftRevision.MinimumTextQuoteWords}.");
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

    private static void CheckPhrases(ImmutableArray<ExecutiveActionSummarySentence> sentences, List<string> errors)
    {
        foreach (var sentence in sentences)
            if (ExecutiveSummaryPhrases.FindIn(sentence.Sentence) is { } phrase)
                errors.Add(ExecutiveSummaryPhrases.Reason(Shorten(sentence.Sentence), phrase));
    }

    private static void CheckGrade(ImmutableArray<ExecutiveActionSummarySentence> sentences, List<string> errors)
    {
        if (sentences.IsEmpty || ExecutiveSummaryApproval.IsPassageReadable(sentences, out var grade)) return;
        var hardest = sentences.MaxBy(sentence => ReadingGrade.Of(sentence.Sentence))!.Sentence;
        errors.Add($"The sentences read at grade {ReadingGrade.Format(grade)} as one passage. Write at grade {ReadingGrade.MaximumGrade} or below. The hardest sentence is \"{Shorten(hardest)}\" at grade {ReadingGrade.Format(ReadingGrade.Rounded(hardest))}. Use shorter words and shorter sentences. Keep the summary at {ExecutiveActionSummary.MinimumSentences} to {ExecutiveActionSummary.MaximumSentences} sentences.");
    }
}
