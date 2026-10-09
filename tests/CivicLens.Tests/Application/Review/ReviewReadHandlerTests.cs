using CivicLens.Application.Review;
using CivicLens.Core.Review;

namespace CivicLens.Tests.Application.Review;

public sealed class ReviewReadHandlerTests
{
    private static readonly ReviewActor Reviewer = new("auth0|reviewer", ReviewRole.Reviewer);
    private static readonly ReviewCatalog Catalog = new([], []);

    // The store is null: a tampered cursor must be refused at the trust boundary before any read.
    [Theory]
    [InlineData("garbage")]
    [InlineData("o.1.a b")]
    [InlineData("")]
    public async Task DraftListRejectsTamperedCursorBeforeReading(string cursor)
    {
        var handler = new ListDocumentChangeReviews(null!, Catalog);

        await Assert.ThrowsAsync<ArgumentException>(() => handler.ExecuteAsync(Reviewer, cursor, 10));
    }

    [Fact]
    public async Task EligibleListRejectsTamperedCursorBeforeReading()
    {
        var handler = new ListEligibleDocumentComparisons(null!, Catalog);

        await Assert.ThrowsAsync<ArgumentException>(() => handler.ExecuteAsync(Reviewer, "n.-5.abc", 10));
    }

    [Fact]
    public async Task DraftListRejectsUnknownFilterAndBadLimit()
    {
        var handler = new ListDocumentChangeReviews(null!, Catalog);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => handler.ExecuteAsync(Reviewer, null, 10, (DraftStatusFilter)99));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => handler.ExecuteAsync(Reviewer, null, 0));
    }

    [Fact]
    public async Task ListsAndOverviewRequireAReviewer()
    {
        var outsider = new ReviewActor("auth0|nobody", (ReviewRole)99);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => new ListDocumentChangeReviews(null!, Catalog).ExecuteAsync(outsider, null, 10));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => new ListEligibleDocumentComparisons(null!, Catalog).ExecuteAsync(outsider, null, 10));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => new GetReviewOverview(null!).ExecuteAsync(outsider));
    }
}
