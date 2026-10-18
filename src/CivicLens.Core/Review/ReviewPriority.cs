namespace CivicLens.Core.Review;

public static class ReviewPriority
{
    public const int Highest = 1;
    public const int Lowest = 3;

    public static int Of(bool wouldPair, bool hasTrackedIssue) => wouldPair ? Highest : hasTrackedIssue ? Highest + 1 : Lowest;
}
