using CivicLens.Application.Actions;
using CivicLens.Application.Collection;
using CivicLens.Application.Collection.Bills;
using CivicLens.Core.Review;
using CivicLens.Tests.Application.Actions;
using CivicLens.Tests.Fixtures;

namespace CivicLens.Tests.Application.Collection.Bills;

public sealed class BackfillBillTextTests
{
    private static readonly ReviewActor Owner = new("auth0|owner", ReviewRole.Owner);
    private static readonly CollectionConfigurationRevision WithText = CollectionConfigurationRevision.Create(BillTextFixtures.Configuration());

    [Fact]
    public async Task APendingActionDraftRefusesTheRunAndAdmitsNothing()
    {
        var store = new RecordingStore([Target("detail")]);

        var result = await Backfill(store, draftPending: true).ExecuteAsync(Owner, 10);

        Assert.Equal((1, 0, 0), Counts(result));
        Assert.Empty(store.Admitted);
        Assert.Null(store.ListedLimit);
    }

    [Fact]
    public async Task TheIdListRunIsRefusedTooWhileADraftIsPending()
    {
        var store = new RecordingStore([Target("detail")]);

        var result = await Backfill(store, draftPending: true).ExecuteAsync(Owner, [new(119, "hr", 7, default)]);

        Assert.Equal((1, 0, 0), Counts(result));
        Assert.Empty(store.Admitted);
        Assert.Null(store.ListedBills);
    }

    [Fact]
    public async Task TheLimitGoesToTheStoreAndEachBillIsAdmittedUnderTheRevisionOfTheCurrentConfiguration()
    {
        var store = new RecordingStore([Target("first"), Target("second")]);

        var result = await Backfill(store).ExecuteAsync(Owner, 2);

        Assert.Equal((0, 2, 2), Counts(result));
        Assert.Empty(result.Skipped);
        Assert.Equal(2, store.ListedLimit);
        Assert.Equal(["first", "second"], store.Admitted.Select(call => call.DetailJobId));
        Assert.All(store.Admitted, call => Assert.Equal((WithText.Id, "congress-bills"), (call.Source.Revision.Id, call.Source.SourceId)));
    }

    [Fact]
    public async Task ABillThatTheStoreDoesNotAdmitIsExaminedButNotCounted()
    {
        var store = new RecordingStore([Target("first"), Target("second")]) { AdmitsOnly = "second" };

        var result = await Backfill(store).ExecuteAsync(Owner, 5);

        Assert.Equal((0, 2, 1), Counts(result));
        Assert.Equal([new SkippedBill("119-hr-first", BillTextSkipReason.NotAdmitted)], result.Skipped);
    }

    [Fact]
    public async Task TheIdListGoesToTheStoreAsGiven()
    {
        var store = new RecordingStore([Target("listed")]);
        CongressBillListItem[] bills = [new(119, "hr", 7, default), new(119, "s", 9, default)];

        var result = await Backfill(store).ExecuteAsync(Owner, bills);

        Assert.Equal((0, 1, 1), Counts(result));
        Assert.Equal(bills, store.ListedBills);
        Assert.Equal([new SkippedBill("119-s-9", BillTextSkipReason.HasSummary)], result.Skipped);
    }

    [Fact]
    public async Task AConfigurationWithoutAnEnabledTextSourceIsRefusedBeforeAnyRead()
    {
        var store = new RecordingStore([Target("detail")]);
        var disabled = CollectionConfigurationRevision.Create(BillTextFixtures.Configuration(enabled: false));

        await Assert.ThrowsAsync<ArgumentException>(() => new BackfillBillText(store, Drafts(), disabled).ExecuteAsync(Owner, 10));

        Assert.False(store.Listed);
        Assert.Empty(store.Admitted);
    }

    [Fact]
    public async Task ASourceThatTheConfigurationDoesNotHoldFailsBeforeAnyAdmission()
    {
        var store = new RecordingStore([Target("first"), new("second", "gone-source", "119-hr-second")]);

        var failure = await Assert.ThrowsAsync<ArgumentException>(() => Backfill(store).ExecuteAsync(Owner, 5));

        Assert.Contains("gone-source", failure.Message);
        Assert.Empty(store.Admitted);
    }

    [Fact]
    public async Task OnlyTheOwnerRunsItTheLimitMustBePositiveAndTheListMustHoldABill()
    {
        var store = new RecordingStore([]);
        var backfill = Backfill(store);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => backfill.ExecuteAsync(new ReviewActor("auth0|reviewer", ReviewRole.Reviewer), 10));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => backfill.ExecuteAsync(Owner, 0));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => backfill.ExecuteAsync(Owner, []));
    }

    private static BillTextBackfillTarget Target(string detail) => new(detail, "congress-bills", "119-hr-" + detail);

    private static BackfillBillText Backfill(RecordingStore store, bool draftPending = false) => new(store, Drafts(draftPending), WithText);

    private static FakeActionDraftStore Drafts(bool pending = false)
    {
        var drafts = new FakeActionDraftStore(ActionBillWorld.Create().Evidence());
        if (!pending) drafts.Drain();
        return drafts;
    }

    private static (int Pending, int Examined, int Admitted) Counts(BillTextBackfillResult result) =>
        (result.PendingActionDrafts, result.Examined, result.Admitted);

    private sealed class RecordingStore(BillTextBackfillTarget[] targets) : IBillTextBackfillStore
    {
        public string? AdmitsOnly { get; init; }
        public bool Listed { get; private set; }
        public int? ListedLimit { get; private set; }
        public IReadOnlyList<CongressBillListItem>? ListedBills { get; private set; }
        public List<(ConfiguredCollectionSource Source, string DetailJobId)> Admitted { get; } = [];

        public Task<IReadOnlyList<BillTextBackfillTarget>> ListNewestBillsWithoutTextAsync(int limit, CancellationToken cancellationToken)
        {
            Listed = true;
            ListedLimit = limit;
            return Task.FromResult<IReadOnlyList<BillTextBackfillTarget>>(targets);
        }

        public Task<BillTextListing> ListNewestBillsWithoutTextAsync(IReadOnlyList<CongressBillListItem> bills, CancellationToken cancellationToken)
        {
            Listed = true;
            ListedBills = bills;
            return Task.FromResult(new BillTextListing(targets, [new("119-s-9", BillTextSkipReason.HasSummary)]));
        }

        public Task<bool> AdmitTextVersionsChainAsync(ConfiguredCollectionSource source, string detailJobId, CancellationToken cancellationToken)
        {
            Admitted.Add((source, detailJobId));
            return Task.FromResult(AdmitsOnly is null || AdmitsOnly == detailJobId);
        }
    }
}
