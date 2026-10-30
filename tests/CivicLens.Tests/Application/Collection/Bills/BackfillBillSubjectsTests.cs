using CivicLens.Application.Collection;
using CivicLens.Application.Collection.Bills;
using CivicLens.Application.Collection.Processing;
using CivicLens.Core.Review;
using CivicLens.Tests.Fixtures;

namespace CivicLens.Tests.Application.Collection.Bills;

public sealed class BackfillBillSubjectsTests
{
    private static readonly ReviewActor Owner = new("auth0|owner", ReviewRole.Owner);

    [Fact]
    public async Task TheCountComesFromTheSavedDetailCaptureAndGoesToTheStoreWithTheSourceOfTheDetailJob()
    {
        var world = ActionBillWorld.Create(subjectPages: [["Housing finance and home ownership"]]);
        var store = new RecordingStore(["detail"]);

        var result = await Backfill(world, store).ExecuteAsync(Owner, 10);

        Assert.Equal(new BillSubjectsBackfillResult(1, 1, 0), result);
        var call = Assert.Single(store.Calls);
        Assert.Equal(("detail", 2, ActionBillWorld.SourceId), (call.DetailJobId, call.SubjectCount, call.Source.SourceId));
        Assert.Equal(10, store.ListedLimit);
    }

    [Fact]
    public async Task ABillThatTheStoreDoesNotAdmitIsExaminedButNotCounted()
    {
        var world = ActionBillWorld.Create(subjectPages: [[]]);
        var store = new RecordingStore(["detail"]) { Admits = false };

        var result = await Backfill(world, store).ExecuteAsync(Owner, 10);

        Assert.Equal(new BillSubjectsBackfillResult(1, 0, 0), result);
        Assert.Equal(1, Assert.Single(store.Calls).SubjectCount);
    }

    [Fact]
    public async Task AMissingOrUnreadableDetailCaptureCountsAsFailedAndTheRunContinuesWithTheNextBill()
    {
        var world = ActionBillWorld.Create(subjectPages: [["Housing finance and home ownership"]]);
        var unreadable = ActionBillWorld.Create(subjectPages: [["Housing finance and home ownership"]]);
        unreadable.ReplaceCapturedBody("detail", "not json");
        var missing = new RecordingStore(["no-such-job", "detail"]);
        var corrupt = new RecordingStore(["detail"]);

        var missingResult = await Backfill(world, missing).ExecuteAsync(Owner, 10);
        var corruptResult = await Backfill(unreadable, corrupt).ExecuteAsync(Owner, 10);

        Assert.Equal(new BillSubjectsBackfillResult(2, 1, 1), missingResult);
        Assert.Equal(new BillSubjectsBackfillResult(1, 0, 1), corruptResult);
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

    private static BackfillBillSubjects Backfill(ActionBillWorld world, RecordingStore store) =>
        new(store, world.Stores, new ApiResponseReader(world.Stores, world.Stores), "artifacts");

    private sealed class RecordingStore(string[] detailJobIds) : IBillSubjectsBackfillStore
    {
        public bool Admits { get; init; } = true;
        public int? ListedLimit { get; private set; }
        public List<(ConfiguredCollectionSource Source, string DetailJobId, int SubjectCount)> Calls { get; } = [];

        public Task<IReadOnlyList<string>> ListNewestBillsWithoutSubjectCountAsync(int limit, CancellationToken cancellationToken)
        {
            ListedLimit = limit;
            return Task.FromResult<IReadOnlyList<string>>(detailJobIds);
        }

        public Task<bool> AdmitSubjectsChainAsync(ConfiguredCollectionSource source, string detailJobId, int subjectCount,
            CancellationToken cancellationToken)
        {
            Calls.Add((source, detailJobId, subjectCount));
            return Task.FromResult(Admits);
        }
    }
}
