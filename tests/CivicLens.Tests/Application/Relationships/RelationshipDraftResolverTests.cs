using CivicLens.Application.Relationships;
using CivicLens.Core.Relationships;
using CivicLens.Core.Review;
using static CivicLens.Tests.Fixtures.RelationshipFixtures;

namespace CivicLens.Tests.Application.Relationships;

public sealed class RelationshipDraftResolverTests
{
    private static readonly DateTimeOffset Now = new(2025, 3, 6, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void AnAcceptedAnswerBecomesRevisionOneBoundToTheExactRevisionsOfBothSides()
    {
        var resolution = Resolve(Output());

        var revision = resolution.Accepted!;
        Assert.True(resolution.IsAccepted);
        Assert.Equal((1, ReviewAuthor.AnalysisSubject, RelationshipType.DescribesOwnBill, "F2"),
            (revision.RevisionNumber, revision.AuthorSubject, revision.Type, revision.TargetFactId));
        Assert.Equal([(RelationshipRecordKind.Statement, StatementId, 1), (RelationshipRecordKind.Action, ActionId, 1)],
            new[] { revision.Statement, revision.Target }.Select(citation => (citation.Kind, citation.RecordId, citation.RevisionNumber)));
        Assert.Equal(BillSentence, Article.Substring(revision.StatementQuote.Start, revision.StatementQuote.Length));
        Assert.Equal(BillWords, Article.Substring(revision.Match.Span.Start, revision.Match.Span.Length));
    }

    [Fact]
    public void TheCodeFoundMatchAndNotTheModelFixesTheMatchedSpan()
    {
        var revision = Resolve(Output(quote: "I introduced H.R. 1234 to cap rent.")).Accepted!;

        Assert.Equal(ActionPair().Match, revision.Match);
    }

    [Fact]
    public void AQuoteThatLeavesOutTheMatchedWordsIsRejectedAsACitationError()
    {
        var resolution = Resolve(Output(quote: "to cap rent."));

        Assert.False(resolution.IsAccepted);
        Assert.True(resolution.HasCitationErrors);
        Assert.Contains("must include the matched words", resolution.Errors.Single(), StringComparison.Ordinal);
    }

    [Fact]
    public void AQuoteFromTheArticleOutsideEveryPassageOfTheStatementIsRejected()
    {
        var resolution = Resolve(Output(quote: FillerSentence));

        Assert.Contains("inside one passage of the statement", resolution.Errors.Single(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("I never wrote this.", "does not occur in the article text")]
    [InlineData("", "needs 1 to")]
    public void AQuoteThatIsNotVerbatimOrIsEmptyIsRejected(string quote, string problem) =>
        Assert.Contains(problem, Assert.Single(Resolve(Output(quote: quote)).Errors), StringComparison.Ordinal);

    [Fact]
    public void ATypedAnswerWithoutAQuoteIsRejected() =>
        Assert.False(Resolve(Output(quote: null)).IsAccepted);

    [Fact]
    public void AnActionAnswerMayNotUseTheVoteType()
    {
        var resolution = Resolve(Output("statementAboutVote"));

        Assert.False(resolution.IsAccepted);
        Assert.Contains("type must be", resolution.Errors.Single(), StringComparison.Ordinal);
    }

    [Fact]
    public void AVoteAnswerMayUseOnlyTheVoteType()
    {
        var pair = VotePair();

        Assert.True(Resolve(Output("statementAboutVote", factId: "F2"), pair).IsAccepted);
        Assert.False(Resolve(Output("claimsCreditFor", factId: "F2"), pair).IsAccepted);
    }

    [Fact]
    public void ANoneAnswerIsAcceptedWithNoRevisionSoTheQueueRowSettlesWithNoRecord()
    {
        var resolution = Resolve(None());

        Assert.True(resolution.IsAccepted);
        Assert.Null(resolution.Accepted);
    }

    [Fact]
    public void AFactIdThatTheRecordDoesNotListIsRejected()
    {
        var resolution = Resolve(Output(factId: "F9"));

        Assert.True(resolution.HasCitationErrors);
        Assert.Contains("not a listed fact ID", resolution.Errors.Single(), StringComparison.Ordinal);
    }

    [Fact]
    public void TheReasonMayNameOnlyTheOfficialsThatTheRecordsList()
    {
        Assert.True(Resolve(Output(reason: "Elizabeth Warren says she wrote the bill.")).IsAccepted);

        var resolution = Resolve(Output(reason: "Ed Markey also supports the bill."));

        Assert.Contains("Ed Markey", resolution.Errors.Single(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Two\nlines.")]
    [InlineData("")]
    public void TheReasonIsOneNonEmptyLine(string reason) => Assert.False(Resolve(Output(reason: reason)).IsAccepted);

    [Fact]
    public void AnOutputThatIsNotJsonForTheSchemaIsRejected() =>
        Assert.False(Resolve("""{"type":"describesOwnBill"}""").IsAccepted);

    [Fact]
    public void ANewAnalysisRevisionFollowsTheCurrentRevisionNumber()
    {
        var first = Resolve(Output()).Accepted!;

        var second = RelationshipDraftResolver.Resolve(Output(), Context(ActionPair(), first), Now).Accepted!;

        Assert.Equal(2, second.RevisionNumber);
    }

    private static CivicLens.Application.Analysis.AnalysisResolution<RelationshipRevision?> Resolve(string output, RelationshipPair? pair = null) =>
        RelationshipDraftResolver.Resolve(output, Context(pair ?? ActionPair()), Now);
}
