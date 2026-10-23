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

    private static async Task<(PublicationScenario Scenario, PublishedRecordReference Action)> PublishedActionAsync()
    {
        var scenario = new PublicationScenario();
        var action = new PublishedRecordReference(PublishedRecordKind.Action, scenario.AddAction('c'));
        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k1", action));
        return (scenario, action);
    }
}
