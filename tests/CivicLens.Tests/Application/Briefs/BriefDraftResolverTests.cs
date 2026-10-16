using CivicLens.Application.Briefs;
using CivicLens.Core.Briefs;
using CivicLens.Core.Review;
using CivicLens.Tests.Fixtures;
using static CivicLens.Tests.Fixtures.BriefFixtures;

namespace CivicLens.Tests.Application.Briefs;

public sealed class BriefDraftResolverTests
{
    private static readonly string RunId = new('7', 32);

    private static readonly BriefRecord[] Records =
    [
        Statement(new string('1', 64), "Warren on rent", 1, "warren"),
        DocumentChange(new string('2', 32), "A rule changed", 3),
        Statement(new string('3', 64), "Markey on heat", 1, "markey")
    ];

    [Fact]
    public void AnAnswerThatCitesListedIdsBecomesRevisionOneWithOnlyTheCitedRecordsAndHonestCoverage()
    {
        var resolution = Resolve(Output(paragraphs: [("Warren spoke.", [Id(0)]), ("A rule changed.", [Id(1), Id(0)])]));

        var revision = resolution.Accepted!;
        Assert.True(resolution.IsAccepted);
        Assert.Equal((1, ReviewAuthor.AnalysisSubject, ReviewTextOrigin.Model), (revision.RevisionNumber, revision.AuthorSubject, revision.TextOrigin));
        Assert.Equal(new[] { Id(0), Id(1) }.Order(), revision.Citations.Select(citation => citation.Id));
        var rule = revision.Citations.Single(citation => citation.Kind == BriefCitationKind.DocumentChange);
        Assert.Equal((Records[1].RecordId, 3), (rule.RecordId, rule.RevisionNumber));
        Assert.Equal((3, 3, 2, 0), (revision.Coverage.Considered, revision.Coverage.SentToModel, revision.Coverage.Cited, revision.Coverage.Omitted));
        Assert.Equal(Context().Fingerprint, revision.Fingerprint);
    }

    [Fact]
    public void TheModelNeedNotCiteEveryRecordSentToIt()
    {
        var resolution = Resolve(Output(paragraphs: [("Only one.", [Id(2)])]));

        Assert.Equal([Id(2)], resolution.Accepted!.Citations.Select(citation => citation.Id));
    }

    [Fact]
    public void ConsideredCountsRecordsTheByteCapLeftOutSoTheCoverageShowsWhatTheBriefOmits()
    {
        var context = BriefDraftContext.Create(Evidence(Records), BriefSelection.Select(Records, sent => sent.Length <= 2), Officials);

        var revision = BriefDraftResolver.Resolve(Output(paragraphs: [("Text.", ["R1"])]), context, RunId, Created).Accepted!;

        Assert.Equal((3, 2, 1, 1), (revision.Coverage.Considered, revision.Coverage.SentToModel, revision.Coverage.Cited, revision.Coverage.Omitted));
    }

    [Fact]
    public void RecordsLeftOutBeforeTheSelectionStillCountAsConsideredSoTheCoverageShowsThem()
    {
        var context = BriefDraftContext.Create(Evidence(Records, leftOut: 2), SelectAll(Records), Officials);

        var revision = BriefDraftResolver.Resolve(Output(paragraphs: [("Text.", ["R1"])]), context, RunId, Created).Accepted!;

        Assert.Equal((5, 3, 1, 2), (revision.Coverage.Considered, revision.Coverage.SentToModel, revision.Coverage.Cited, revision.Coverage.Omitted));
    }

    [Fact]
    public void AnIdThatWasNeverListedIsRejectedAsACitationError()
    {
        var resolution = Resolve(Output(paragraphs: [("Text.", ["R1", "R9"])]));

        Assert.False(resolution.IsAccepted);
        Assert.True(resolution.HasCitationErrors);
        Assert.Contains(resolution.Errors, error => error.Contains("\"R9\"", StringComparison.Ordinal));
    }

    [Fact]
    public void AnIdRepeatedInOneParagraphIsRejected()
    {
        var resolution = Resolve(Output(paragraphs: [("Text.", ["R1", "R1"])]));

        Assert.True(resolution.HasCitationErrors);
        Assert.Contains(resolution.Errors, error => error.Contains("more than once", StringComparison.Ordinal));
    }

    [Fact]
    public void AParagraphWithoutAnIdIsRejected()
    {
        var resolution = Resolve(Output(paragraphs: [("Text.", [])]));

        Assert.True(resolution.HasCitationErrors);
        Assert.Contains(resolution.Errors, error => error.Contains("cites no ID", StringComparison.Ordinal));
    }

    [Fact]
    public void TheSameIdInTwoParagraphsIsFine()
    {
        var resolution = Resolve(Output(paragraphs: [("One.", ["R1"]), ("Two.", ["R1"])]));

        Assert.True(resolution.IsAccepted);
    }

    [Fact]
    public void ATextThatNamesAConfiguredPersonWhoIsNotAnOfficialInTheSentRecordsIsRejected()
    {
        var resolution = Resolve(Output(paragraphs: [("Bernie Sanders also spoke.", ["R1"])]));

        Assert.False(resolution.IsAccepted);
        Assert.False(resolution.HasCitationErrors);
        Assert.Contains(resolution.Errors, error => error.Contains("who is not an official in the facts", StringComparison.Ordinal));
    }

    [Fact]
    public void ThePersonWhoAppearsInTheSentRecordsMayBeNamedInTheHeadlineAndTheParagraphs()
    {
        var resolution = Resolve(Output("Elizabeth Warren and Ed Markey spoke", 0.5, ("Elizabeth Warren spoke.", ["R1"])));

        Assert.True(resolution.IsAccepted);
    }

    [Fact]
    public void ARecordLeftOutByTheCutDoesNotCountAsAnOfficialInTheFacts()
    {
        var context = BriefDraftContext.Create(Evidence(Records), BriefSelection.Select(Records, sent => sent.Length <= 1), Officials);

        var resolution = BriefDraftResolver.Resolve(Output("Ed Markey spoke", 0.5, ("Text.", ["R1"])), context, RunId, Created);

        Assert.False(resolution.IsAccepted);
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.5)]
    public void AConfidenceOutsideZeroToOneIsRejected(double confidence)
    {
        Assert.False(Resolve(Output(confidence: confidence)).IsAccepted);
    }

    [Fact]
    public void TextsAreBoundedAndNeverEmpty()
    {
        Assert.False(Resolve(Output(new string('h', BriefRevision.MaximumHeadlineLength + 1))).IsAccepted);
        Assert.False(Resolve(Output(" ")).IsAccepted);
        Assert.False(Resolve(Output(paragraphs: [(new string('p', BriefRevision.MaximumParagraphLength + 1), ["R1"])])).IsAccepted);
        Assert.False(Resolve("""{"headline":"h","paragraphs":[],"confidence":0.5}""").IsAccepted);
    }

    [Fact]
    public void OutputThatIsNotValidForTheSchemaIsRejectedWithoutAnException()
    {
        Assert.False(Resolve("not json").IsAccepted);
        Assert.False(Resolve("""{"headline":"h","paragraphs":[{"text":"t","citationIds":["R1"],"extra":1}],"confidence":0.5}""").IsAccepted);
    }

    [Fact]
    public void ARevisionAfterTheFirstTakesTheNextNumber()
    {
        var current = Resolve(Output()).Accepted!;
        var context = Context(current);

        var next = BriefDraftResolver.Resolve(Output(), context, RunId, Created).Accepted!;

        Assert.Equal(2, next.RevisionNumber);
    }

    [Fact]
    public void WhenAPersonWroteTheCurrentTextTheModelTextOnlyBecomesASuggestionWithItsOwnCitations()
    {
        var model = Resolve(Output(paragraphs: [("Model text.", [Id(2)])])).Accepted!;
        var person = model with { AuthorSubject = "auth0|editor", TextOrigin = ReviewTextOrigin.Person, Headline = "Edited", Paragraphs = [new("Edited.", [Id(2)])] };

        var next = BriefDraftResolver.Resolve(Output(paragraphs: [("Newer model text.", [Id(0)])]), Context(person), RunId, Created).Accepted!;

        Assert.Equal((ReviewTextOrigin.Person, "Edited", person.Citations), (next.TextOrigin, next.Headline, next.Citations));
        Assert.Equal("Newer model text.", Assert.Single(next.Suggestion!.Paragraphs).Text);
        Assert.Equal([Id(0)], next.Suggestion.Citations.Select(citation => citation.Id));
        Assert.Equal(RunId, next.Suggestion.RunId);
    }

    private static BriefDraftContext Context(BriefRevision? current = null) => BriefFixtures.Context(Records, current);

    private static string Id(int index) => Context().Selection.Sent.Single(item => item.Record == Records[index]).CitationId;

    private static CivicLens.Application.Analysis.AnalysisResolution<BriefRevision> Resolve(string json) =>
        BriefDraftResolver.Resolve(json, Context(), RunId, Created);

    private static string Output(string headline = "A week of housing news", double confidence = 0.8,
        params (string Text, string[] Ids)[] paragraphs) => BriefFixtures.Output(headline, confidence, paragraphs);
}
