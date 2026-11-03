using System.Collections.Immutable;
using CivicLens.Application.ExecutiveActions;
using CivicLens.Core.Review;

namespace CivicLens.Tests.Application.ExecutiveActions;

public sealed class DecideExecutiveActionReviewTests
{
    private static readonly ReviewActor Reviewer = new("auth0|friend", ReviewRole.Reviewer);
    private static readonly string RecordId = new('a', 64);

    private readonly FakeExecutiveActionReviewStore store = new();

    [Fact]
    public async Task AnOutsiderOrTheCodeSubjectClosesNoReportBecauseTheReviewerCheckComesBeforeTheStore()
    {
        var request = Request([Guid.NewGuid()]);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            new DecideExecutiveActionReview(store).ExecuteAsync(new("auth0|nobody", (ReviewRole)99), request));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            new DecideExecutiveActionReview(store).ExecuteAsync(new(ReviewAuthor.CodeSubject, ReviewRole.Owner), request));

        Assert.Empty(store.Decided);
    }

    [Fact]
    public async Task OneApprovalNamesAtMost64ReportIdsAndTheListIsCountedBeforeRepeatsAreRemoved()
    {
        var ids = Enumerable.Range(0, 65).Select(_ => Guid.NewGuid()).ToArray();

        await Assert.ThrowsAsync<ArgumentException>(() => new DecideExecutiveActionReview(store).ExecuteAsync(Reviewer, Request([.. ids])));
        Assert.Empty(store.Decided);

        await new DecideExecutiveActionReview(store).ExecuteAsync(Reviewer, Request([.. ids.Take(64)]));

        Assert.Equal(ids.Take(64), Assert.Single(store.ReportIds));
    }

    private static DecideExecutiveActionReviewRequest Request(ImmutableArray<Guid> reportIds) =>
        new(RecordId, 1, 0, ReviewDecisionKind.Approve, null, [], "key", null, reportIds);
}
