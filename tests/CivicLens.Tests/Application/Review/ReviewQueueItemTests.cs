using CivicLens.Application.Paging;
using CivicLens.Application.Review;
using CivicLens.Tests.Fixtures;
using CivicLens.Core.Review;
using CivicLens.Publication.Contracts;

namespace CivicLens.Tests.Application.Review;

public sealed class ReviewQueueItemTests
{
    private static ReviewQueueItem Item(PublishedRecordKind kind, ReviewStatus status, int concerns = 0, string? held = null) =>
        new(kind, "id", "Headline", status, concerns, held, DateTimeOffset.UnixEpoch);

    [Fact]
    public void AHeldReasonIsTheReasonBecauseItNamesTheCheckThatCodeApprovalFailed()
    {
        var item = Item(PublishedRecordKind.Action, ReviewStatus.AwaitingReview, held: "The bill title changed.");

        Assert.Equal("The bill title changed.", item.Reason);
    }

    [Fact]
    public void ARecordWithoutAHeldReasonKeepsAnExplanationFromItsStatusAndKind()
    {
        Assert.Equal("Code did not approve this record. A person must decide.",
            Item(PublishedRecordKind.Vote, ReviewStatus.AwaitingReview, held: " ").Reason);
        Assert.Equal("A person must review this draft.", Item(PublishedRecordKind.Brief, ReviewStatus.AwaitingReview).Reason);
        Assert.Equal("1 open concern needs an answer.", Item(PublishedRecordKind.Brief, ReviewStatus.AwaitingReviewWithConcerns, 1).Reason);
        Assert.Equal("3 open concerns need an answer.", Item(PublishedRecordKind.Brief, ReviewStatus.AwaitingReviewWithConcerns, 3).Reason);
        Assert.Equal("A reviewer asked for changes. Revise the draft.", Item(PublishedRecordKind.Statement, ReviewStatus.ChangesRequested).Reason);
        Assert.Equal("A reviewer withdrew the approval. Review the draft again.",
            Item(PublishedRecordKind.Statement, ReviewStatus.ApprovalWithdrawn).Reason);
    }

    [Fact]
    public void ARecordThatAPersonEditedSaysNoCheckRanBecauseCodeApprovalSkipsPersonText()
    {
        var edited = Item(PublishedRecordKind.Action, ReviewStatus.AwaitingReview) with { EditedByPerson = true };

        Assert.Equal("A person edited this record. A person must approve it.", edited.Reason);
        Assert.Equal("A person must review this draft.", (edited with { Kind = PublishedRecordKind.Brief }).Reason);
    }

    [Fact]
    public void ARecordApprovedAfterTheListWasReadTellsTheReaderToRefresh()
    {
        Assert.Equal("This record is approved. Refresh the page.", Item(PublishedRecordKind.Statement, ReviewStatus.Approved).Reason);
    }

    [Fact]
    public void AnUnreadableRecordSaysPlainlyThatItCannotBeReadAndSendsTheOwnerToTheLogsBecauseTheEditorShowsNoError()
    {
        var item = ReviewQueueItem.Unreadable(PublishedRecordKind.Vote, "abc", DateTimeOffset.UnixEpoch, new InvalidOperationException("bad json"));

        Assert.Equal("abc", item.Headline);
        Assert.Equal("This record cannot be read, so its status is not known. The owner must check the logs for this record ID.",
            item.Reason);
    }

    [Fact]
    public async Task TheQueueHandlerLogsEachUnreadableRecordWithItsIdBecauseTheRowHidesTheCause()
    {
        var cause = new InvalidOperationException("Stored revision is invalid.");
        var page = new ReviewQueuePage([ReviewQueueItem.Unreadable(PublishedRecordKind.Vote, "vote-1", DateTimeOffset.UnixEpoch, cause),
            Item(PublishedRecordKind.Action, ReviewStatus.AwaitingReview)], 2, null, null);
        var logger = new CapturingLogger<ListReviewQueue>();

        await new ListReviewQueue(new StubStore(page), logger).ExecuteAsync(new ReviewActor("auth0|reviewer", ReviewRole.Reviewer), null);

        var entry = Assert.Single(logger.Entries);
        Assert.Contains("vote-1", entry.Message, StringComparison.Ordinal);
        Assert.Same(cause, entry.Exception);
    }

    private sealed class StubStore(ReviewQueuePage page) : IReviewQueueStore
    {
        public Task<ReviewQueuePage> ListAsync(PageCursor? cursor, int limit, CancellationToken cancellationToken) => Task.FromResult(page);

        public Task<ApprovedRecordPage> SearchApprovedAsync(string text, PageCursor? cursor, int limit, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
