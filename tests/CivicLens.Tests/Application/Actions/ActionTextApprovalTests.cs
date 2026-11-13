using System.Collections.Immutable;
using CivicLens.Application.Actions;
using CivicLens.Core.Actions;
using CivicLens.Core.Documents;
using CivicLens.Core.Review;
using CivicLens.Tests.Fixtures;
using static CivicLens.Tests.Fixtures.ActionBillTexts;

namespace CivicLens.Tests.Application.Actions;

public sealed class ActionTextApprovalTests
{
    private const string Plain = "The bill lets the Secretary pay landlords for tenants with low income.";

    private static readonly ActionDraftBillText Text = Of();
    private static readonly IReadOnlyDictionary<string, DocumentExtraction> Extractions =
        new Dictionary<string, DocumentExtraction> { [ExtractionId] = Extraction(Text) };

    [Fact]
    public void ModelTextThatQuotesTheBodyAndReadsAtOrBelowTheGradeLimitIsApprovedByTheChecks()
    {
        ActionTextApproval.Validate(Revision("H.R. 1 pays landlords for tenants.", Plain), Extractions);
    }

    [Fact]
    public void ModelTextAboveTheGradeLimitFailsTheApprovalChecksAndNamesTheGrade()
    {
        var hard = "Comprehensive reauthorization notwithstanding administrative reconsideration necessitates multidimensional intergovernmental accountability.";

        var error = Assert.Throws<ArgumentException>(() => ActionTextApproval.Validate(Revision("H.R. 1 reauthorizes housing assistance.", hard), Extractions));

        Assert.Contains("read at grade 14.1 as one passage", error.Message, StringComparison.Ordinal);
        Assert.Contains("limit is grade 14.0", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ModelTextAboveTheTargetGradeAndAtOrBelowTheMaximumIsApprovedByTheChecks()
    {
        var hard = "Comprehensive reauthorization notwithstanding administrative reconsideration necessitates multidimensional intergovernmental accountability.";
        var revision = Revision("H.R. 1 renews housing assistance.", hard);

        ActionTextApproval.Validate(revision, Extractions);

        Assert.Equal(13.6, ReadingGrade.RoundedPassage([revision.Headline, .. revision.TextCitations.Select(citation => citation.Sentence)]));
    }

    [Fact]
    public void WordingThatAPersonWroteHasNoGradeCheckBecauseAPersonDecides()
    {
        var edited = ActionRevisionFixtures.PersonEditOf(Revision("H.R. 1 pays landlords.", Plain), string.Join(" ", Enumerable.Repeat("comprehensive reauthorization notwithstanding administrative reconsideration necessitates multidimensional accountability", 2)),
            Revision("H.R. 1 pays landlords.", Plain).Summary);

        ActionTextApproval.Validate(edited, Extractions);
    }

    [Fact]
    public void AQuoteOfFewerThanFourWordsFailsTheApprovalChecks()
    {
        var revision = Revision("H.R. 1 pays landlords.", Plain);
        var start = Text.Text.IndexOf("Rent Aid Act", StringComparison.Ordinal);
        var short_ = revision with { TextCitations = revision.TextCitations.SetItem(0, revision.TextCitations[0] with { Span = new(ExtractionId, start, "Rent Aid Act".Length) }) };

        var error = Assert.Throws<ArgumentException>(() => ActionTextApproval.Validate(short_, Extractions));

        Assert.Contains("at least 4 words", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AQuoteInTheHeaderOfTheExtractionFailsTheApprovalChecksBecauseOnlyTheBodyIsBillText()
    {
        var revision = Revision("H.R. 1 pays landlords.", Plain);
        var header = revision with { TextCitations = revision.TextCitations.SetItem(0, revision.TextCitations[0] with { Span = new(ExtractionId, 0, 20) }) };

        var error = Assert.Throws<ArgumentException>(() => ActionTextApproval.Validate(header, Extractions));

        Assert.Contains("body", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ACitationOfAnExtractionThatWasNotReadFailsTheApprovalChecks()
    {
        var revision = Revision("H.R. 1 pays landlords.", Plain);

        Assert.Throws<ArgumentException>(() => ActionTextApproval.Validate(revision, new Dictionary<string, DocumentExtraction>()));
    }

    // One summary sentence of the given plain text, one more, and one why it matters sentence. Each quotes a different span of the body.
    private static ActionDraftRevision Revision(string headline, string firstSentence)
    {
        ActionTextCitation Cite(ActionTextPart part, string quote, string sentence) =>
            new(part, new(ExtractionId, Text.Text.IndexOf(quote, StringComparison.Ordinal), quote.Length), sentence);
        ImmutableArray<ActionTextCitation> citations =
        [
            Cite(ActionTextPart.Summary, FirstQuote, firstSentence),
            Cite(ActionTextPart.Summary, SecondQuote, SecondPlain),
            Cite(ActionTextPart.WhyItMatters, WhyQuote, WhyPlain)
        ];
        var sponsorship = ActionRevisionFixtures.Sponsorship() with { Citations = [new(ExtractionId, 0, 10), new(ExtractionId, 11, 10)] };
        return ActionRevisionFixtures.Model() with
        {
            Headline = headline,
            Summary = ActionTextCitation.Join(citations.Take(2)),
            WhyItMatters = WhyPlain,
            TextPackageId = PackageId,
            TextCitations = citations,
            Sponsorships = [sponsorship]
        };
    }
}
