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
        Assert.Equal("This record has 20 corrections with a note, the most it can hold. " +
            "A person revision cannot publish. Only a code-approved revision can.", listed.StuckText());
    }

    [Fact]
    public async Task ThePublisherRefusesAPersonRevisionAtTheCapAndTheListHoldsTheSameRecordFromOneState()
    {
        var (scenario, action) = await PublishedAtCapAsync(PublishedCorrection.MaximumNotedEntries);
        scenario.ReviseAction(action.RecordId, PublishedCorrection.MaximumNotedEntries + 2, correctionNote: "One too many.");
        ApproveNextRevision(scenario, action, "auth0|editor");

        await Assert.ThrowsAsync<CorrectionLimitException>(() =>
            scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("over", action)));
        Assert.Equal(action.RecordId, Assert.Single(await scenario.BlockedLister().ExecuteAsync(PublicationScenario.Owner)).RecordId);
    }

    [Theory]
    [InlineData(PublishedRecordKind.Action, true)]
    [InlineData(PublishedRecordKind.Vote, true)]
    [InlineData(PublishedRecordKind.Statement, true)]
    [InlineData(PublishedRecordKind.ExecutiveAction, true)]
    [InlineData(PublishedRecordKind.DocumentChange, false)]
    [InlineData(PublishedRecordKind.Brief, false)]
    [InlineData(PublishedRecordKind.Relationship, false)]
    public void TheReasonTextOffersCodeApprovalOnlyForAKindThatCodeCanApprove(PublishedRecordKind kind, bool codeCanApprove)
    {
        var record = new BlockedPublishedRecord(kind, "id", "Headline", 1, ReviewStatus.Approved, BlockedReason.CorrectionLimit);

        var expected = codeCanApprove
            ? "This record has 20 corrections with a note, the most it can hold. A person revision cannot publish. Only a code-approved revision can."
            : "This record has 20 corrections with a note, the most it can hold. No new revision can publish.";
        Assert.Equal(expected, record.StuckText());
    }

    [Fact]
    public async Task AWithdrawnRecordAtTheCapWithAPersonRevisionIsListedBecauseItCannotReturn()
    {
        var (scenario, action) = await PublishedAtCapAsync(PublishedCorrection.MaximumNotedEntries);
        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner,
            new PublishReleaseRequest([], "w") { Withdrawals = [new(action.Kind, action.RecordId, "Wrong.")] });
        ApproveNextRevision(scenario, action, "auth0|editor");

        var listed = Assert.Single(await scenario.BlockedLister().ExecuteAsync(PublicationScenario.Owner));

        Assert.Equal((action.RecordId, BlockedReason.WithdrawnAtCorrectionLimit, PublishedCorrection.MaximumNotedEntries + 1),
            (listed.RecordId, listed.Reason, listed.PublishedRevisionNumber));
        Assert.Contains(action.RecordId, listed.Headline, StringComparison.Ordinal);
        Assert.Equal("This record is off the site and has 20 corrections with a note, the most it can hold. " +
            "It cannot return to the site.", listed.StuckText());
    }

    [Fact]
    public async Task ARollbackToAReleaseWithAWithdrawnRecordAtTheCapReportsItBecauseItStillCannotReturn()
    {
        var (scenario, action) = await PublishedAtCapAsync(PublishedCorrection.MaximumNotedEntries);
        var withdrawal = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner,
            new PublishReleaseRequest([], "w") { Withdrawals = [new(action.Kind, action.RecordId, "Wrong.")] });
        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner,
            PublicationScenario.Request("later", scenario.AddDraft('a')));
        ApproveNextRevision(scenario, action, "auth0|editor");

        var result = await scenario.Activator().ExecuteAsync(PublicationScenario.Owner, withdrawal.ReleaseNumber);

        var listed = Assert.Single(result.Blocked);
        Assert.Equal((action.RecordId, BlockedReason.WithdrawnAtCorrectionLimit), (listed.RecordId, listed.Reason));
    }

    [Theory]
    [InlineData(PublishedCorrection.MaximumNotedEntries - 1, "auth0|editor")]
    [InlineData(PublishedCorrection.MaximumNotedEntries, ReviewAuthor.CodeSubject)]
    public async Task AWithdrawnRecordIsNotListedBelowTheCapOrForACodeRevisionBecauseOnlyAPersonRevisionAtTheCapIsStuck(int notedCorrections,
        string actor)
    {
        var (scenario, action) = await PublishedAtCapAsync(notedCorrections);
        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner,
            new PublishReleaseRequest([], "w") { Withdrawals = [new(action.Kind, action.RecordId, "Wrong.")] });
        ApproveNextRevision(scenario, action, actor);

        Assert.Empty(await scenario.BlockedLister().ExecuteAsync(PublicationScenario.Owner));
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
    public async Task ANoticeBlockedOnlyByReviewStatusIsNotListedBecauseTheRecordAlreadyLeftTheRelease()
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
        scenario.Publications.UnpublishedApprovalActors[action] = actor;
    }

    private static async Task<(PublicationScenario Scenario, PublishedRecordReference Action)> PublishedActionAsync()
    {
        var scenario = new PublicationScenario();
        var action = new PublishedRecordReference(PublishedRecordKind.Action, scenario.AddAction('c'));
        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k1", action));
        return (scenario, action);
    }
}
