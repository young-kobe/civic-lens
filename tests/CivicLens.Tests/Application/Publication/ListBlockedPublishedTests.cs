using CivicLens.Application.Publication;
using CivicLens.Core.Review;
using CivicLens.Publication.Contracts;

namespace CivicLens.Tests.Application.Publication;

public sealed class ListBlockedPublishedTests
{
    [Theory]
    [InlineData(ReviewStatus.ApprovalWithdrawn)]
    [InlineData(ReviewStatus.ChangesRequested)]
    [InlineData(ReviewStatus.AwaitingReviewWithConcerns)]
    public async Task ARecordThatReviewBlockedIsListedWithWhatTheOwnerNeedsToWithdrawIt(ReviewStatus status)
    {
        var (scenario, action) = await PublishedActionAsync();
        scenario.Publications.ReviewStatuses[action] = status;

        var listed = Assert.Single(await scenario.BlockedLister().ExecuteAsync(PublicationScenario.Owner));

        Assert.Equal((PublishedRecordKind.Action, action.RecordId, 1, status),
            (listed.Kind, listed.RecordId, listed.PublishedRevisionNumber, listed.ReviewStatus));
        Assert.False(string.IsNullOrWhiteSpace(listed.Headline));
        Assert.Equal(BlockedReason.ReviewBlocksIt, listed.Reason);
    }

    [Fact]
    public async Task APersonRevisionStuckAtTheCorrectionCapIsListedWithTheReasonBecauseTheWorkerSkipsItForever()
    {
        var (scenario, action) = await PublishedAtCapAsync(PublishedCorrection.MaximumNotedEntries);
        ApproveNextRevision(scenario, action, "auth0|editor");

        var listed = Assert.Single(await scenario.BlockedLister().ExecuteAsync(PublicationScenario.Owner));

        Assert.Equal((action.RecordId, BlockedReason.CorrectionLimit, ReviewStatus.Approved),
            (listed.RecordId, listed.Reason, listed.ReviewStatus));
        Assert.Contains($"{PublishedCorrection.MaximumNotedEntries} corrections with a note", BlockedPublishedRecord.CorrectionLimitText,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARecordBelowTheCorrectionCapIsNotListedBecauseAPersonRevisionStillPublishes()
    {
        var (scenario, action) = await PublishedAtCapAsync(PublishedCorrection.MaximumNotedEntries - 1);
        ApproveNextRevision(scenario, action, "auth0|editor");

        Assert.Empty(await scenario.BlockedLister().ExecuteAsync(PublicationScenario.Owner));
    }

    [Fact]
    public async Task ACodeApprovedRevisionAtTheCorrectionCapIsNotListedBecauseCodeUpdatesAlwaysPublish()
    {
        var (scenario, action) = await PublishedAtCapAsync(PublishedCorrection.MaximumNotedEntries);
        ApproveNextRevision(scenario, action, ReviewAuthor.CodeSubject);

        Assert.Empty(await scenario.BlockedLister().ExecuteAsync(PublicationScenario.Owner));
    }

    [Fact]
    public async Task OneStatusReadAndOneRecordFileReadForTheCandidateKeepTheListBounded()
    {
        var (scenario, action) = await PublishedAtCapAsync(PublishedCorrection.MaximumNotedEntries);
        ApproveNextRevision(scenario, action, "auth0|editor");
        scenario.Publications.StatusReads.Clear();
        scenario.Releases.ReadPaths.Clear();

        await scenario.BlockedLister().ExecuteAsync(PublicationScenario.Owner);

        Assert.Single(scenario.Publications.StatusReads);
        Assert.Equal([PublicationProtocol.ManifestPath, PublicationProtocol.RecordDataPath(action.RecordId)], scenario.Releases.ReadPaths);
    }

    [Fact]
    public async Task ARecordWhoseApprovedRevisionTheReleaseHoldsCostsNoRecordFileRead()
    {
        var (scenario, action) = await PublishedAtCapAsync(PublishedCorrection.MaximumNotedEntries);
        scenario.Publications.ReviewStatuses[action] = ReviewStatus.Approved;
        scenario.Releases.ReadPaths.Clear();

        Assert.Empty(await scenario.BlockedLister().ExecuteAsync(PublicationScenario.Owner));
        Assert.Equal([PublicationProtocol.ManifestPath], scenario.Releases.ReadPaths);
    }

    [Theory]
    [InlineData(ReviewStatus.Approved)]
    [InlineData(ReviewStatus.AwaitingReview)]
    [InlineData(ReviewStatus.NotDrafted)]
    public async Task ARecordThatReviewStillAllowsIsNotListedBecauseWithdrawingItWouldBeRefused(ReviewStatus status)
    {
        var (scenario, action) = await PublishedActionAsync();
        scenario.Publications.ReviewStatuses[action] = status;

        Assert.Empty(await scenario.BlockedLister().ExecuteAsync(PublicationScenario.Owner));
    }

    [Fact]
    public async Task ARecordWithNoReviewRowIsNotListed()
    {
        var (scenario, _) = await PublishedActionAsync();

        Assert.Empty(await scenario.BlockedLister().ExecuteAsync(PublicationScenario.Owner));
    }

    [Fact]
    public async Task ANoticeForAWithdrawnRecordIsNotListedBecauseTheRecordAlreadyLeftTheRelease()
    {
        var (scenario, action) = await PublishedActionAsync();
        scenario.Publications.ReviewStatuses[action] = ReviewStatus.ApprovalWithdrawn;
        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner,
            new PublishReleaseRequest([], "k2") { Withdrawals = [new(action.Kind, action.RecordId, "Wrong.")] });

        Assert.Empty(await scenario.BlockedLister().ExecuteAsync(PublicationScenario.Owner));
    }

    [Fact]
    public async Task OnlyTheOwnerMayListBlockedRecordsBecauseOnlyTheOwnerMayWithdraw()
    {
        var (scenario, _) = await PublishedActionAsync();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            scenario.BlockedLister().ExecuteAsync(PublicationScenario.Reviewer));
    }

    [Fact]
    public async Task NoActiveReleaseGivesAnEmptyList()
    {
        var scenario = new PublicationScenario();

        Assert.Empty(await scenario.BlockedLister().ExecuteAsync(PublicationScenario.Owner));
    }

    private static async Task<(PublicationScenario Scenario, PublishedRecordReference Action)> PublishedAtCapAsync(int notedCorrections)
    {
        var (scenario, action) = await PublishedActionAsync();
        for (var revision = 2; revision <= notedCorrections + 1; revision++)
        {
            scenario.ReviseAction(action.RecordId, revision, correctionNote: "Fix.");
            await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner,
                PublicationScenario.Request($"k{revision}", action));
        }
        return (scenario, action);
    }

    private static void ApproveNextRevision(PublicationScenario scenario, PublishedRecordReference action, string actor)
    {
        scenario.Publications.ReviewStatuses[action] = ReviewStatus.Approved;
        scenario.Publications.UnpublishedApprovals[action] = (PublishedCorrection.MaximumNotedEntries + 2, actor);
    }

    private static async Task<(PublicationScenario Scenario, PublishedRecordReference Action)> PublishedActionAsync()
    {
        var scenario = new PublicationScenario();
        var action = new PublishedRecordReference(PublishedRecordKind.Action, scenario.AddAction('c'));
        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k1", action));
        return (scenario, action);
    }
}
