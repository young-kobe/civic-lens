using System.Collections.Immutable;
using CivicLens.Application.Actions;
using CivicLens.Application.Review;
using CivicLens.Application.Votes;
using CivicLens.Core.Actions;
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

    [Fact]
    public async Task AVoteRowShowsAnApprovalCheckThatFailsWhenACitedExtractionIsMissingSoTheReviewerSeesItBeforeDeciding()
    {
        var vote = RelationshipFixtures.Vote();
        var store = new FakeVoteReviewStore
        {
            Triage = new([new(vote.RecordId, vote, null, [vote], 0, ReviewStatus.AwaitingReview, [], [])], NoExtractions, null, "older")
        };

        var page = await new ListVoteTriage(store).ExecuteAsync(Reviewer, null);

        var row = Assert.Single(page.Rows);
        Assert.False(row.Check.Passed);
        Assert.Contains("missing extraction", row.Check.Failure);
        Assert.Equal("older", page.OlderCursor);
    }

    [Fact]
    public async Task AnActionRowRunsTheSameApprovalValidationAsApprove()
    {
        var action = RelationshipFixtures.Action() with { Headline = "" };
        var store = new FakeActionReviewStore
        {
            Triage = new([new(action.RecordId, action, null, [action], 0, ReviewStatus.AwaitingReview, [], [])], NoExtractions, null, null)
        };

        var row = Assert.Single((await new ListActionTriage(store).ExecuteAsync(Reviewer, null)).Rows);

        Assert.False(row.Check.Passed);
        Assert.Equal(action.RecordId, row.Review.RecordId);
    }

    [Fact]
    public async Task TriageRefusesANonReviewerAndATamperedCursorBeforeAnyRead()
    {
        var outsider = new ReviewActor("auth0|nobody", (ReviewRole)99);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => new ListVoteTriage(null!).ExecuteAsync(outsider, null));
        await Assert.ThrowsAsync<ArgumentException>(() => new ListVoteTriage(null!).ExecuteAsync(Reviewer, "garbage"));
        await Assert.ThrowsAsync<ArgumentException>(() => new ListActionTriage(null!).ExecuteAsync(Reviewer, "garbage"));
    }
}
