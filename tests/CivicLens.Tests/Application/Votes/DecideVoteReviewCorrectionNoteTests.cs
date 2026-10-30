using System.Collections.Immutable;
using CivicLens.Application.Review;
using CivicLens.Application.Votes;
using CivicLens.Core.Review;
using CivicLens.Publication.Contracts;

namespace CivicLens.Tests.Application.Votes;

public sealed class DecideVoteReviewCorrectionNoteTests
{
    private static readonly ReviewActor Reviewer = new("auth0|reviewer", ReviewRole.Reviewer);

    [Fact]
    public async Task AnApprovalKeepsTheTrimmedCorrectionNoteSoThePublishStepCanWriteItIntoTheRecordHistory()
    {
        var (store, decide) = Decider();

        await decide.ExecuteAsync(Reviewer, Request(ReviewDecisionKind.Approve, "  Fixed the roll call.\r\nSecond line.  "));

        Assert.Equal("Fixed the roll call.\nSecond line.", Assert.Single(store.Decided).CorrectionNote);
    }

    [Fact]
    public async Task AnApprovalWithoutACorrectionNoteKeepsNone()
    {
        var (store, decide) = Decider();

        await decide.ExecuteAsync(Reviewer, Request(ReviewDecisionKind.Approve, null));

        Assert.Null(Assert.Single(store.Decided).CorrectionNote);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("bad\0note")]
    public async Task ABlankOrNulCorrectionNoteIsRefused(string note)
    {
        var (store, decide) = Decider();

        await Assert.ThrowsAsync<ArgumentException>(() => decide.ExecuteAsync(Reviewer, Request(ReviewDecisionKind.Approve, note)));

        Assert.Empty(store.Decided);
    }

    [Fact]
    public async Task ACorrectionNoteOverTheContractLengthIsRefusedAndTheLimitLengthIsAccepted()
    {
        var (store, decide) = Decider();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            decide.ExecuteAsync(Reviewer, Request(ReviewDecisionKind.Approve, new string('a', PublishedCorrection.MaximumNoteLength + 1))));
        await decide.ExecuteAsync(Reviewer, Request(ReviewDecisionKind.Approve, new string('a', PublishedCorrection.MaximumNoteLength)));

        Assert.Single(store.Decided);
    }

    [Fact]
    public async Task OnlyAnApprovalTakesACorrectionNoteBecauseNothingElseReachesAReader()
    {
        var (store, decide) = Decider();

        await Assert.ThrowsAsync<ArgumentException>(() => decide.ExecuteAsync(Reviewer, Request(ReviewDecisionKind.RequestChanges, "Fix.")));

        Assert.Empty(store.Decided);
    }

    private static (FakeVoteReviewStore Store, DecideVoteReview Decide) Decider()
    {
        var store = new FakeVoteReviewStore();
        return (store, new DecideVoteReview(store));
    }

    private static DecideVoteReviewRequest Request(ReviewDecisionKind kind, string? correctionNote) =>
        new(new string('a', 64), 1, 1, kind, "Needs work.", ImmutableArray<string>.Empty, "key", correctionNote);
}
