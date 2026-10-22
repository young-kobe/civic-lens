using CivicLens.Core.Review;

namespace CivicLens.Tests.Core.Review;

public sealed class ReviewAuthorTests
{
    private static readonly DateTimeOffset Utc = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ARevisionHeaderNeedsANumberAnAuthorOfOneTo256CharactersAndAUtcTimeSoEveryRevisionKindSharesOneRule()
    {
        Assert.True(ReviewAuthor.IsValidRevisionHeader(1, "auth0|reviewer", Utc));
        Assert.True(ReviewAuthor.IsValidRevisionHeader(1, new string('a', ReviewAuthor.MaximumSubjectLength), Utc));

        Assert.False(ReviewAuthor.IsValidRevisionHeader(0, "auth0|reviewer", Utc));
        Assert.False(ReviewAuthor.IsValidRevisionHeader(1, " ", Utc));
        Assert.False(ReviewAuthor.IsValidRevisionHeader(1, null, Utc));
        Assert.False(ReviewAuthor.IsValidRevisionHeader(1, new string('a', ReviewAuthor.MaximumSubjectLength + 1), Utc));
        Assert.False(ReviewAuthor.IsValidRevisionHeader(1, "auth0|reviewer", Utc.ToOffset(TimeSpan.FromHours(2))));
    }
}
