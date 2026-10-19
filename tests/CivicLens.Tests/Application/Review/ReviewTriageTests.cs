using System.Collections.Immutable;
using CivicLens.Application.Actions;
using CivicLens.Application.Review;
using CivicLens.Application.Votes;
using CivicLens.Collection.Contracts;
using CivicLens.Core.Actions;
using CivicLens.Core.Collection;
using CivicLens.Core.Documents;
using CivicLens.Core.Review;
using CivicLens.Core.Votes;
using CivicLens.Tests.Application.Actions;
using CivicLens.Tests.Application.Votes;
using CivicLens.Tests.Fixtures;

namespace CivicLens.Tests.Application.Review;

public sealed class ReviewTriageTests
{
    private static readonly ReviewActor Reviewer = new("auth0|reviewer", ReviewRole.Reviewer);
    private static readonly ImmutableDictionary<string, DocumentExtraction> NoExtractions = ImmutableDictionary<string, DocumentExtraction>.Empty;

    private static ImmutableDictionary<string, int> Priorities(string recordId) =>
        ImmutableDictionary<string, int>.Empty.Add(recordId, ReviewPriority.Highest);

    [Fact]
    public async Task AVoteRowOnValidEvidenceCarriesItsQuotesAndAPassingCheck()
    {
        var vote = RelationshipFixtures.Vote();

        var row = Assert.Single((await VoteTriage(vote, ValidText)).Rows);

        Assert.True(row.Check.Passed);
        Assert.Equal(["Vote day", "Vote kind", "Fact", "Fact", "Fact"], row.Quotes.Select(quote => quote.Label));
        Assert.Equal(ValidText[..5], row.Quotes[0].Text);
        Assert.Equal(ReviewPriority.Highest, row.Priority);
    }

    [Fact]
    public async Task AVoteRowWithABadShapeFailsWithTheShapeMessageEvenWhenEveryExtractionIsPresentAndShowsNoQuote()
    {
        var vote = RelationshipFixtures.Vote() with { Headline = "" };
        var expected = Assert.Throws<ArgumentException>(vote.ValidateForApproval).Message;

        var row = Assert.Single((await VoteTriage(vote, ValidText)).Rows);

        Assert.Equal(expected, row.Check.Failure);
        Assert.Empty(row.Quotes);
    }

    [Fact]
    public async Task AVoteRowWhoseCitedExtractionIsMissingFailsTheCheckAndShowsNoQuoteSoOneBadRowNeverBreaksThePage()
    {
        var row = Assert.Single((await VoteTriage(RelationshipFixtures.Vote(), null)).Rows);

        Assert.Contains("missing extraction", row.Check.Failure);
        Assert.Empty(row.Quotes);
    }

    [Fact]
    public async Task AnActionRowRunsTheSameApprovalValidationAsApproveOnValidEvidenceAndLabelsTheQuoteWithTheSponsorRole()
    {
        var action = RelationshipFixtures.Action();
        var shape = action with { Headline = "" };
        var expected = Assert.Throws<ArgumentException>(shape.ValidateForApproval).Message;

        var passing = Assert.Single((await ActionTriage(action, ValidText)).Rows);
        var failing = Assert.Single((await ActionTriage(shape, ValidText)).Rows);

        Assert.True(passing.Check.Passed);
        var quote = Assert.Single(passing.Quotes);
        Assert.Equal(("Sponsor", ValidText[..10]), (quote.Label, quote.Text));
        Assert.Equal(expected, failing.Check.Failure);
        Assert.Empty(failing.Quotes);
    }

    private const string ValidText = "0123456789 0123456789 0123456789 0123456789";

    private static ImmutableDictionary<string, DocumentExtraction> Extractions(string? text) => text is null ? NoExtractions
        : ImmutableDictionary<string, DocumentExtraction>.Empty.Add(RelationshipFixtures.ExtractionId, ExtractionOf(text));

    private static DocumentExtraction ExtractionOf(string text) => new(new CapturedAttemptResult("attempt", "source", "https://example.test/",
        "https://example.test/", DateTimeOffset.UnixEpoch, new CollectionResponse(200, null, null, "text/plain", []),
        new CaptureIdentity(new string('a', 64), 0)), "parser", "normalizer", text);

    private static Task<ReviewTriagePage<VoteReview>> VoteTriage(VoteDraftRevision vote, string? text) => new ListVoteTriage(new FakeVoteReviewStore
    {
        Triage = new([new(vote.RecordId, vote, null, [vote], 0, ReviewStatus.AwaitingReview, [], [])], Extractions(text),
            Priorities(vote.RecordId), null, null)
    }).ExecuteAsync(Reviewer, null);

    private static Task<ReviewTriagePage<ActionReview>> ActionTriage(ActionDraftRevision action, string? text) => new ListActionTriage(
        new FakeActionReviewStore
        {
            Triage = new([new(action.RecordId, action, null, [action], 0, ReviewStatus.AwaitingReview, [], [])], Extractions(text),
                Priorities(action.RecordId), null, null)
        }).ExecuteAsync(Reviewer, null);

    [Fact]
    public async Task TriageRefusesANonReviewerAndATamperedCursorBeforeAnyRead()
    {
        var outsider = new ReviewActor("auth0|nobody", (ReviewRole)99);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => new ListVoteTriage(null!).ExecuteAsync(outsider, null));
        await Assert.ThrowsAsync<ArgumentException>(() => new ListVoteTriage(null!).ExecuteAsync(Reviewer, "garbage"));
        await Assert.ThrowsAsync<ArgumentException>(() => new ListActionTriage(null!).ExecuteAsync(Reviewer, "garbage"));
    }
}
