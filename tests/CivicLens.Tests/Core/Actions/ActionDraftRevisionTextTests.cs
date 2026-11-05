using System.Collections.Immutable;
using CivicLens.Core.Actions;
using CivicLens.Core.Review;
using static CivicLens.Tests.Fixtures.ActionRevisionFixtures;

namespace CivicLens.Tests.Core.Actions;

public sealed class ActionDraftRevisionTextTests
{
    [Fact]
    public void ARevisionDraftedFromTheBillTextHoldsTwoOrThreeSummarySentencesThenAWhyItMattersPartEachWithACitation()
    {
        TextModel().ValidateAsWorkerRevision(null);

        Assert.Equal(TextModel().TextCitations.Select(citation => citation.Span), TextModel().Citations.TakeLast(3));
    }

    [Fact]
    public void ARevisionNeverCitesTheCrsSummaryAndTheBillTextTogetherBecauseOneRevisionHasOneSource()
    {
        var both = TextModel() with { SummaryVersion = "00@2025-01-05T10:00:00Z", SummaryCitation = new(ExtractionId, 0, 10) };

        var error = Assert.Throws<ArgumentException>(() => both.ValidateShape());

        Assert.Contains("never both", error.Message, StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => (TextModel() with { SummaryCitation = new(ExtractionId, 0, 10) }).ValidateShape());
        (Model() with { SummaryVersion = "00@2025-01-05T10:00:00Z", SummaryCitation = new(ExtractionId, 0, 10) }).ValidateShape();
    }

    [Fact]
    public void ThePackageTheWhyItMattersPartAndTheCitationsComeTogether()
    {
        Assert.Throws<ArgumentException>(() => (TextModel() with { TextPackageId = null }).ValidateShape());
        Assert.Throws<ArgumentException>(() => (TextModel() with { WhyItMatters = null }).ValidateShape());
        Assert.Throws<ArgumentException>(() => (TextModel() with { TextCitations = default }).ValidateShape());
        Assert.Throws<ArgumentException>(() => (Model() with { WhyItMatters = "Orphan." }).ValidateShape());
        Assert.Throws<ArgumentException>(() => (Model() with { TextCitations = TextModel().TextCitations }).ValidateShape());
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(4, 1)]
    [InlineData(2, 0)]
    [InlineData(2, 4)]
    public void TheSentenceCountsOfBothPartsAreBounded(int summary, int why)
    {
        var citations = Enumerable.Range(0, summary).Select(index => new ActionTextCitation(ActionTextPart.Summary, new(ExtractionId, index * 10, 5), "A sentence."))
            .Concat(Enumerable.Range(0, why).Select(index => new ActionTextCitation(ActionTextPart.WhyItMatters, new(ExtractionId, 100 + index * 10, 5), "A sentence.")));

        Assert.Throws<ArgumentException>(() => (TextModel() with { TextCitations = [.. citations] }).ValidateShape());
    }

    [Fact]
    public void TheSummarySentencesComeBeforeTheWhyItMattersSentences()
    {
        var swapped = TextModel().TextCitations.Reverse().ToImmutableArray();

        Assert.Throws<ArgumentException>(() => (TextModel() with { TextCitations = swapped }).ValidateShape());
    }

    [Fact]
    public void AQuoteLongerThanTheCapIsRefused()
    {
        var long_ = TextModel().TextCitations.SetItem(0, new(ActionTextPart.Summary, new(ExtractionId, 0, ActionDraftRevision.MaximumTextQuoteLength + 1), "First sentence."));

        Assert.Throws<ArgumentException>(() => (TextModel() with { TextCitations = long_ }).ValidateShape());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ASentenceMustHaveTextAndStayUnderItsCap(string sentence)
    {
        Assert.Throws<ArgumentException>(() => (TextModel() with { TextCitations = TextModel().TextCitations.SetItem(0, TextModel().TextCitations[0] with { Sentence = sentence }) }).ValidateShape());
        var long_ = new string('a', ActionDraftRevision.MaximumTextSentenceLength + 1);
        Assert.Throws<ArgumentException>(() => (TextModel() with { TextCitations = TextModel().TextCitations.SetItem(0, TextModel().TextCitations[0] with { Sentence = long_ }) }).ValidateShape());
    }

    [Fact]
    public void APersonMayEditTheWordsOfARevisionButNotItsTextSourceOrCitations()
    {
        var edited = PersonEditOf(TextModel(), "Edited headline", "Edited summary.");

        edited.ValidatePersonEditOf(TextModel());
        Assert.Throws<ArgumentException>(() => (edited with { TextPackageId = "BILLS-119hr1rh" }).ValidatePersonEditOf(TextModel()));
        Assert.Throws<ArgumentException>(() => (edited with { WhyItMatters = "Another." }).ValidatePersonEditOf(TextModel()));
        Assert.Throws<ArgumentException>(() => (edited with { TextCitations = TextModel().TextCitations.RemoveAt(2) }).ValidatePersonEditOf(TextModel()));
        Assert.Equal(ReviewTextOrigin.Person, edited.TextOrigin);
    }
}
