using CivicLens.Core.Review;

namespace CivicLens.Host.Review;

internal static class PriorityLabels
{
    public static string? Of(int priority) => priority switch
    {
        ReviewPriority.Highest => "Pairs with a statement",
        ReviewPriority.Highest + 1 => "Tracked issue",
        _ => null
    };
}
