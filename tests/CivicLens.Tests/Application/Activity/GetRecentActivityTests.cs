using CivicLens.Application.Activity;
using CivicLens.Application.Collection.Jobs;
using CivicLens.Application.Collection.Processing;
using CivicLens.Application.Review;
using CivicLens.Core.Review;
using CivicLens.Tests.Application.Publication;
using CivicLens.Tests.Fixtures;

namespace CivicLens.Tests.Application.Activity;

public sealed class GetRecentActivityTests
{
    private static readonly ReviewActor Owner = new("auth0|owner", ReviewRole.Owner);
    private static readonly ReviewActor Reviewer = new("auth0|reviewer", ReviewRole.Reviewer);
    private static readonly DateTimeOffset Noon = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task OwnerSeesReviewAndCollectionEventsMergedNewestFirst()
    {
        var handler = Handler(out _, out _, out _);

        var events = await handler.ExecuteAsync(Owner, 10);

        Assert.Equal(
            [ActivityKind.ChangeFound, ActivityKind.DecisionRecorded, ActivityKind.CheckFailed, ActivityKind.RevisionSaved,
             ActivityKind.CheckStarted, ActivityKind.DraftCreated],
            events.Select(item => item.Kind));
        Assert.Equal(events.OrderByDescending(item => item.OccurredAt).Select(item => item.OccurredAt), events.Select(item => item.OccurredAt));
    }

    [Fact]
    public async Task OnlyTheNewestRequestedEventsSurviveTheMerge()
    {
        var handler = Handler(out _, out _, out _);

        var events = await handler.ExecuteAsync(Owner, 3);

        Assert.Equal([ActivityKind.ChangeFound, ActivityKind.DecisionRecorded, ActivityKind.CheckFailed],
            events.Select(item => item.Kind));
    }

    [Fact]
    public async Task ReviewerSeesReviewEventsOnlyAndCollectionStoresAreNeverRead()
    {
        var handler = Handler(out _, out var jobs, out var processing);

        var events = await handler.ExecuteAsync(Reviewer, 10);

        Assert.Equal([ActivityKind.DecisionRecorded, ActivityKind.RevisionSaved, ActivityKind.DraftCreated],
            events.Select(item => item.Kind));
        Assert.Equal(0, jobs.ActivityReads);
        Assert.Equal(0, processing.ChangeReads);
    }

    [Fact]
    public async Task EventsCarryTheFieldsTheirKindPromises()
    {
        var handler = Handler(out _, out _, out _);

        var events = (await handler.ExecuteAsync(Owner, 10)).ToDictionary(item => item.Kind);

        var decision = events[ActivityKind.DecisionRecorded];
        Assert.Equal(("auth0|reviewer", "draft-1", "Rule change", ReviewDecisionKind.RequestChanges),
            (decision.ActorSubject, decision.DraftId, decision.Headline, decision.DecisionKind));
        Assert.Null(decision.SourceId);
        var found = events[ActivityKind.ChangeFound];
        Assert.Equal(("source-a", "job-3", new string('c', 64)), (found.SourceId, found.JobId, found.ComparisonId));
        Assert.Null(found.ActorSubject);
        Assert.Equal("source-b", events[ActivityKind.CheckFailed].SourceId);
    }

    [Fact]
    public async Task SimultaneousEventsKeepAStableOrder()
    {
        var reviews = new FakeReviewStore();
        reviews.Activity.AddRange([Review(ReviewActivityKind.RevisionSaved, Noon, "b"), Review(ReviewActivityKind.DraftCreated, Noon, "a")]);
        var handler = new GetRecentActivity(reviews);

        var first = await handler.ExecuteAsync(Reviewer, 8);
        reviews.Activity.Reverse();
        var second = await handler.ExecuteAsync(Reviewer, 8);

        Assert.Equal(first, second);
        Assert.Equal(ActivityKind.DraftCreated, first[0].Kind);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(51)]
    public async Task LimitOutsideBoundsIsRejected(int limit)
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Handler(out _, out _, out _).ExecuteAsync(Owner, limit));
    }

    [Fact]
    public async Task ActorWithoutAReviewRoleIsRefusedBeforeAnyRead()
    {
        var reviews = new FakeReviewStore();
        var handler = new GetRecentActivity(reviews);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            handler.ExecuteAsync(new ReviewActor("auth0|nobody", (ReviewRole)99)));
    }

    [Fact]
    public async Task DefaultLimitIsEight()
    {
        var reviews = new FakeReviewStore();
        for (var index = 0; index < 12; index++)
            reviews.Activity.Add(Review(ReviewActivityKind.DraftCreated, Noon.AddMinutes(index), "draft-" + index));

        var events = await new GetRecentActivity(reviews).ExecuteAsync(Reviewer);

        Assert.Equal(GetRecentActivity.DefaultLimit, events.Count);
        Assert.Equal(8, events.Count);
    }

    private static GetRecentActivity Handler(out FakeReviewStore reviews, out FixedJobStore jobs, out FixedProcessingStore processing)
    {
        reviews = new FakeReviewStore();
        reviews.Activity.AddRange([
            Review(ReviewActivityKind.DraftCreated, Noon.AddMinutes(-50), "draft-1"),
            Review(ReviewActivityKind.RevisionSaved, Noon.AddMinutes(-30), "draft-1"),
            new(ReviewActivityKind.DecisionRecorded, Noon.AddMinutes(-10), "auth0|reviewer", "draft-1", "Rule change",
                ReviewDecisionKind.RequestChanges)]);
        jobs = new FixedJobStore
        {
            Activity = new([new("job-1", "source-a", Noon.AddMinutes(-40))],
                [new("job-2", "source-b", Noon.AddMinutes(-20))])
        };
        processing = new FixedProcessingStore { Changes = [new("job-3", "source-a", new string('c', 64), Noon)] };
        return new GetRecentActivity(reviews, jobs, processing);
    }

    private static ReviewActivityEvent Review(ReviewActivityKind kind, DateTimeOffset at, string draftId) =>
        new(kind, at, "auth0|author", draftId, "Headline " + draftId, null);
}
