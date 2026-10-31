using CivicLens.Application.Collection.Bills;
using CivicLens.Application.Collection.Processing;
using CivicLens.Core.Review;
using CivicLens.Tests.Fixtures;

namespace CivicLens.Tests.Application.Collection.Bills;

public sealed class BackfillBillStatusTests
{
    private static readonly ReviewActor Owner = new("auth0|owner", ReviewRole.Owner);
    private static readonly CongressBillLatestAction Latest = new(new DateOnly(2025, 3, 2), "Passed House.");

    [Fact]
    public async Task TheStatusComesFromTheSavedDetailCaptureSoTheBackfillMakesNoRequest()
    {
        var world = ActionBillWorld.Create(latestAction: Latest);
        var store = new RecordingStore(["detail"]);

        var result = await Backfill(world, store).ExecuteAsync(Owner, 10);

        Assert.Equal(new BillStatusBackfillResult(1, 1, 0), result);
        Assert.Equal(("detail", Latest), Assert.Single(store.Calls));
        Assert.Equal(10, store.ListedLimit);
    }

    [Fact]
    public async Task ADetailWithNoLatestActionIsRecordedAsReadWithNoStatusSoItLeavesTheList()
    {
        var world = ActionBillWorld.Create();
        var store = new RecordingStore(["detail"]);

        await Backfill(world, store).ExecuteAsync(Owner, 10);

        Assert.Equal(("detail", (CongressBillLatestAction?)null), Assert.Single(store.Calls));
    }

    [Fact]
    public async Task ABillThatTheStoreAlreadyReadIsExaminedButNotCounted()
    {
        var world = ActionBillWorld.Create(latestAction: Latest);
        var store = new RecordingStore(["detail"]) { Records = false };

        Assert.Equal(new BillStatusBackfillResult(1, 0, 0), await Backfill(world, store).ExecuteAsync(Owner, 10));
    }

    [Fact]
    public async Task AMissingOrUnreadableDetailCaptureCountsAsFailedAndTheRunContinuesWithTheNextBill()
    {
        var world = ActionBillWorld.Create(latestAction: Latest);
        var unreadable = ActionBillWorld.Create(latestAction: Latest);
        unreadable.ReplaceCapturedBody("detail", "not json");
        var missing = new RecordingStore(["no-such-job", "detail"]);
        var corrupt = new RecordingStore(["detail"]);

        Assert.Equal(new BillStatusBackfillResult(2, 1, 1), await Backfill(world, missing).ExecuteAsync(Owner, 10));
        Assert.Equal(new BillStatusBackfillResult(1, 0, 1), await Backfill(unreadable, corrupt).ExecuteAsync(Owner, 10));
        Assert.Empty(corrupt.Calls);
    }

    [Fact]
    public async Task OnlyTheOwnerRunsItAndTheLimitMustBePositive()
    {
        var world = ActionBillWorld.Create();
        var store = new RecordingStore([]);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Backfill(world, store).ExecuteAsync(new ReviewActor("auth0|reviewer", ReviewRole.Reviewer), 10));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Backfill(world, store).ExecuteAsync(Owner, 0));
    }

    private static BackfillBillStatus Backfill(ActionBillWorld world, RecordingStore store) =>
        new(store, world.Stores, new ApiResponseReader(world.Stores, world.Stores), "artifacts");

    private sealed class RecordingStore(string[] detailJobIds) : IBillStatusBackfillStore
    {
        public bool Records { get; init; } = true;
        public int? ListedLimit { get; private set; }
        public List<(string DetailJobId, CongressBillLatestAction? LatestAction)> Calls { get; } = [];

        public Task<IReadOnlyList<string>> ListNewestBillsWithoutStatusAsync(int limit, CancellationToken cancellationToken)
        {
            ListedLimit = limit;
            return Task.FromResult<IReadOnlyList<string>>(detailJobIds);
        }

        public Task<bool> RecordStatusAsync(string detailJobId, CongressBillLatestAction? latestAction, CancellationToken cancellationToken)
        {
            Calls.Add((detailJobId, latestAction));
            return Task.FromResult(Records);
        }
    }
}
