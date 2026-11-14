using CivicLens.Core.Analysis;
using CivicLens.Core.ExecutiveActions;
using CivicLens.Core.Review;
using CivicLens.Tests.Fixtures;

namespace CivicLens.Tests.Core.Analysis;

public sealed class QuotedSentenceLimitsTests
{
    private static readonly string ExtractionId = new('c', 64);

    [Fact]
    public void TheBillTextSummaryTheExecutiveSummaryAndTheKeyTermsShareOneSetOfQuotedLimits()
    {
        Assert.Equal((2, 3, 400, 4, 300), (QuotedSentenceLimits.MinimumSentences, QuotedSentenceLimits.MaximumSentences,
            QuotedSentenceLimits.MaximumSentenceLength, QuotedSentenceLimits.MinimumQuoteWords, QuotedSentenceLimits.MaximumQuoteLength));
    }

    [Fact]
    public void TheExecutiveSummaryAndTheBillTextSummaryRejectTheSameSentenceOneCharacterTooLong()
    {
        var atLimit = new string('a', QuotedSentenceLimits.MaximumSentenceLength);
        var tooLong = atLimit + "a";

        Summary(atLimit).ValidateShape();
        Assert.Throws<ArgumentException>(() => Summary(tooLong).ValidateShape());
        var text = ActionRevisionFixtures.TextModel();
        (text with { TextCitations = text.TextCitations.SetItem(0, text.TextCitations[0] with { Sentence = atLimit }) }).ValidateShape();
        Assert.Throws<ArgumentException>(() =>
            (text with { TextCitations = text.TextCitations.SetItem(0, text.TextCitations[0] with { Sentence = tooLong }) }).ValidateShape());
    }

    [Fact]
    public void TheExecutiveSummaryAndTheBillTextSummaryRejectTheSameQuoteOneCharacterTooLong()
    {
        Assert.Throws<ArgumentException>(() => Summary("A sentence.", QuotedSentenceLimits.MaximumQuoteLength + 1).ValidateShape());
        var text = ActionRevisionFixtures.TextModel();
        var citation = text.TextCitations[0];
        Assert.Throws<ArgumentException>(() => (text with
        {
            TextCitations = text.TextCitations.SetItem(0, citation with { Span = citation.Span with { Length = QuotedSentenceLimits.MaximumQuoteLength + 1 } })
        }).ValidateShape());
    }

    private static ExecutiveActionSummary Summary(string first, int quoteLength = 10) => new(ReviewTextOrigin.Model, false,
        [new(first, new(ExtractionId, 0, quoteLength)), new("A second sentence.", new(ExtractionId, 20, 10))]);
}
