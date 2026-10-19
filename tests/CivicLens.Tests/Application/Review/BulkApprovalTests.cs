using System.Collections.Immutable;
using CivicLens.Application.Review;
using CivicLens.Application.Votes;
using CivicLens.Core.Review;
using CivicLens.Tests.Application.Votes;

namespace CivicLens.Tests.Application.Review;

public sealed class BulkApprovalTests
{
    private static readonly ReviewActor Reviewer = new("auth0|reviewer", ReviewRole.Reviewer);
    private readonly ApproveBulkSafeVotes approve = new(new FakeVoteReviewStore());

    [Fact]
    public async Task ABatchNeedsOneToSixtyFourDistinctRecordsBecauseEachRecordCostsOneTransaction()
    {
        BulkApprovalTarget Target(char id) => new(new string(id, 64), 1, 0);
        ImmutableArray<BulkApprovalTarget> Many(int count) =>
            [.. Enumerable.Range(0, count).Select(index => new BulkApprovalTarget(index.ToString("x64"), 1, 0))];

        await Assert.ThrowsAsync<ArgumentException>(() => approve.ExecuteAsync(Reviewer, new([], "key")));
        await Assert.ThrowsAsync<ArgumentException>(() => approve.ExecuteAsync(Reviewer, new(default, "key")));
        await Assert.ThrowsAsync<ArgumentException>(() => approve.ExecuteAsync(Reviewer, new([Target('1'), Target('1')], "key")));
        await Assert.ThrowsAsync<ArgumentException>(() => approve.ExecuteAsync(Reviewer, new(Many(BulkApproval.MaximumRecords + 1), "key")));
    }

    [Fact]
    public async Task TheKeyLeavesRoomForTheRecordIdAndTheCallerMustBeAReviewer()
    {
        var target = new BulkApprovalTarget(new string('1', 64), 1, 0);

        await Assert.ThrowsAsync<ArgumentException>(() => approve.ExecuteAsync(Reviewer, new([target], new string('k', 192))));
        await Assert.ThrowsAsync<ArgumentException>(() => approve.ExecuteAsync(Reviewer, new([target], " ")));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            approve.ExecuteAsync(new("auth0|nobody", (ReviewRole)99), new([target], "key")));
    }
}
