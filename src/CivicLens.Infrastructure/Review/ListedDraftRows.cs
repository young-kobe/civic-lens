using System.Collections.Immutable;
using CivicLens.Infrastructure.Review.Persistence;

namespace CivicLens.Infrastructure.Review;

internal static class ListedDraftRows
{
    public static void Set(IListedDraftRow draft, DateOnly eventOn, ImmutableArray<string> issueIds, ImmutableArray<string> officialIds)
    {
        draft.EventOn = eventOn;
        draft.IssueIds = [.. issueIds];
        draft.OfficialIds = [.. officialIds];
    }
}
