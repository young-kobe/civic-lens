using CivicLens.Application.Collection;
using CivicLens.Application.Collection.Bills;
using CivicLens.Core.Review;
using CivicLens.Tests.Fixtures;

namespace CivicLens.Tests.Application.Collection.Bills;

public sealed class BackfillBillTextTests
{
    private static readonly ReviewActor Owner = new("auth0|owner", ReviewRole.Owner);
    private static readonly CollectionConfigurationRevision WithText = CollectionConfigurationRevision.Create(BillTextFixtures.Configuration());

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public async Task APendingActionDraftRefusesTheRunAndAdmitsNothing(int pending)
    {
        var store = new RecordingStore([new("detail", "congress-bills")]) { Pending = pending };

        var result = await new BackfillBillText(store, WithText).ExecuteAsync(Owner, 10);

        Assert.Equal(new BillTextBackfillResult(pending, 0, 0), result);
        Assert.Empty(store.Admitted);
        Assert.Null(store.ListedLimit);
    }

    [Fact]
    public async Task TheIdListRunIsRefusedTooWhileADraftIsPending()
    {
        var store = new RecordingStore([new("detail", "congress-bills")]) { Pending = 2 };

        var result = await new BackfillBillText(store, WithText).ExecuteAsync(Owner, [new(119, "hr", 7, default)]);

        Assert.Equal(new BillTextBackfillResult(2, 0, 0), result);
        Assert.Empty(store.Admitted);
        Assert.Null(store.ListedBills);
    }

    [Fact]
    public async Task TheLimitGoesToTheStoreAndEachBillIsAdmittedUnderTheRevisionOfTheCurrentConfiguration()
    {
        var store = new RecordingStore([new("first", "congress-bills"), new("second", "congress-bills")]);

        var result = await new BackfillBillText(store, WithText).ExecuteAsync(Owner, 2);

        Assert.Equal(new BillTextBackfillResult(0, 2, 2), result);
        Assert.Equal(2, store.ListedLimit);
        Assert.Equal(["first", "second"], store.Admitted.Select(call => call.DetailJobId));
        Assert.All(store.Admitted, call => Assert.Equal((WithText.Id, "congress-bills"), (call.Source.Revision.Id, call.Source.SourceId)));
    }

    [Fact]
    public async Task ABillThatTheStoreDoesNotAdmitIsExaminedButNotCounted()
    {
        var store = new RecordingStore([new("first", "congress-bills"), new("second", "congress-bills")]) { AdmitsOnly = "second" };

        var result = await new BackfillBillText(store, WithText).ExecuteAsync(Owner, 5);

        Assert.Equal(new BillTextBackfillResult(0, 2, 1), result);
    }

    [Fact]
    public async Task TheIdListGoesToTheStoreAsGiven()
    {
        var store = new RecordingStore([new("listed", "congress-bills")]);
        CongressBillListItem[] bills = [new(119, "hr", 7, default), new(119, "s", 9, default)];

        var result = await new BackfillBillText(store, WithText).ExecuteAsync(Owner, bills);

        Assert.Equal(new BillTextBackfillResult(0, 1, 1), result);
        Assert.Equal(bills, store.ListedBills);
    }

    [Fact]
    public async Task AConfigurationWithoutAnEnabledTextSourceIsRefusedBeforeAnyRead()
    {
        var store = new RecordingStore([new("detail", "congress-bills")]);
        var disabled = CollectionConfigurationRevision.Create(BillTextFixtures.Configuration(enabled: false));

        await Assert.ThrowsAsync<ArgumentException>(() => new BackfillBillText(store, disabled).ExecuteAsync(Owner, 10));

        Assert.False(store.Counted);
        Assert.Empty(store.Admitted);
    }

    [Fact]
    public async Task OnlyTheOwnerRunsItTheLimitMustBePositiveAndTheListMustHoldABill()
    {
        var store = new RecordingStore([]);
        var backfill = new BackfillBillText(store, WithText);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => backfill.ExecuteAsync(new ReviewActor("auth0|reviewer", ReviewRole.Reviewer), 10));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => backfill.ExecuteAsync(Owner, 0));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => backfill.ExecuteAsync(Owner, []));
    }

    private sealed class RecordingStore(BillTextBackfillTarget[] targets) : IBillTextBackfillStore
    {
        public int Pending { get; init; }
        public string? AdmitsOnly { get; init; }
        public bool Counted { get; private set; }
        public int? ListedLimit { get; private set; }
        public IReadOnlyList<CongressBillListItem>? ListedBills { get; private set; }
        public List<(ConfiguredCollectionSource Source, string DetailJobId)> Admitted { get; } = [];

        public Task<int> CountPendingActionDraftsAsync(CancellationToken cancellationToken)
        {
            Counted = true;
            return Task.FromResult(Pending);
        }

        public Task<IReadOnlyList<BillTextBackfillTarget>> ListNewestBillsWithoutTextAsync(int limit, CancellationToken cancellationToken)
        {
            ListedLimit = limit;
            return Task.FromResult<IReadOnlyList<BillTextBackfillTarget>>(targets);
        }

        public Task<IReadOnlyList<BillTextBackfillTarget>> ListNewestBillsWithoutTextAsync(IReadOnlyList<CongressBillListItem> bills,
            CancellationToken cancellationToken)
        {
            ListedBills = bills;
            return Task.FromResult<IReadOnlyList<BillTextBackfillTarget>>(targets);
        }

        public Task<bool> AdmitTextVersionsChainAsync(ConfiguredCollectionSource source, string detailJobId, CancellationToken cancellationToken)
        {
            Admitted.Add((source, detailJobId));
            return Task.FromResult(AdmitsOnly is null || AdmitsOnly == detailJobId);
        }
    }
}
