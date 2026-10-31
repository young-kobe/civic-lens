using CivicLens.Application.Review;
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
    public void NoKindAndStatusPairShowsAnEmptyReasonBecauseTheQueueMustAlwaysSayWhyAPersonIsNeeded()
    {
        foreach (var kind in Enum.GetValues<PublishedRecordKind>())
            foreach (var status in Enum.GetValues<ReviewStatus>())
                Assert.False(string.IsNullOrWhiteSpace(Item(kind, status).Reason), $"{kind} {status}");
    }
}
