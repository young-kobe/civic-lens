using CivicLens.Core.Review;

namespace CivicLens.Tests.Core.Review;

public sealed class ReviewPriorityTests
{
    [Theory]
    [InlineData(true, true, 1)]
    [InlineData(true, false, 1)]
    [InlineData(false, true, 2)]
    [InlineData(false, false, 3)]
    public void AWouldPairRecordOutranksATrackedIssueAndATrackedIssueOutranksTheRest(bool wouldPair, bool hasTrackedIssue, int expected)
    {
        Assert.Equal(expected, ReviewPriority.Of(wouldPair, hasTrackedIssue));
    }
}
