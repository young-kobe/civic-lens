using CivicLens.Application.Review;
using CivicLens.Core.Review;

namespace CivicLens.Tests.Application.Review;

public sealed class ReviewReadHandlerTests
{
    private static readonly ReviewActor Reviewer = new("auth0|reviewer", ReviewRole.Reviewer);
    private static readonly ReviewCatalog Catalog = new([], []);

    [Fact]
    public async Task EligibleListRejectsTamperedCursorBeforeReading()
    {
        var handler = new ListEligibleDocumentComparisons(null!, Catalog);

        await Assert.ThrowsAsync<ArgumentException>(() => handler.ExecuteAsync(Reviewer, "n.-5.abc", 10));
    }

    [Fact]
    public async Task ListRequiresAReviewer()
    {
        var outsider = new ReviewActor("auth0|nobody", (ReviewRole)99);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => new ListEligibleDocumentComparisons(null!, Catalog).ExecuteAsync(outsider, null, 10));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SearchRejectsBlankTextAndTooLongTextBeforeReading(string query)
    {
        var handler = new SearchApprovedRecords(null!);

        await Assert.ThrowsAsync<ArgumentException>(() => handler.ExecuteAsync(Reviewer, query, null));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            handler.ExecuteAsync(Reviewer, new string('a', SearchApprovedRecords.MaximumQueryLength + 1), null));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            handler.ExecuteAsync(new ReviewActor("auth0|nobody", (ReviewRole)99), "rent", null));
    }
}
