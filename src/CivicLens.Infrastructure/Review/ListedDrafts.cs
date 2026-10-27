using CivicLens.Application.Paging;
using CivicLens.Application.Review;
using CivicLens.Core.Review;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Review.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CivicLens.Infrastructure.Review;

/// <summary>The list read of statement drafts: the filters and the page.</summary>
internal static class ListedDrafts
{
    public static Task<KeysetSlice<RankedDraft<TDraftRow>>> PageWithoutTierAsync<TDraftRow>(IQueryable<TDraftRow> drafts,
        string recordIdProperty, PageCursor? cursor, int limit, DraftListFilter filter, CancellationToken cancellationToken)
        where TDraftRow : class, IListedDraftRow =>
        PageAsync(Filter(drafts, filter).Select(draft => new RankedDraft<TDraftRow> { Draft = draft, Priority = ReviewPriority.Highest }),
            recordIdProperty, cursor, limit, cancellationToken);

    private static async Task<KeysetSlice<RankedDraft<TDraftRow>>> PageAsync<TDraftRow>(IQueryable<RankedDraft<TDraftRow>> ranked,
        string recordIdProperty, PageCursor? cursor, int limit, CancellationToken cancellationToken)
        where TDraftRow : class, IListedDraftRow
    {
        var fetched = await ReviewKeyset.Page(ranked, cursor, row => row.Priority, row => row.Draft.EventOn,
            row => EF.Property<string>(row.Draft, recordIdProperty)).Take(limit + 1).ToArrayAsync(cancellationToken);
        return KeysetSlice<RankedDraft<TDraftRow>>.Create(fetched, limit, cursor,
            row => (row.Priority, ReviewKeyset.TicksOf(row.Draft.EventOn), row.Draft.RecordId));
    }

    private static IQueryable<TDraftRow> Filter<TDraftRow>(IQueryable<TDraftRow> drafts, DraftListFilter filter)
        where TDraftRow : class, IListedDraftRow
    {
        if (filter.IssueId is { } issueId) drafts = drafts.Where(draft => draft.IssueIds.Contains(issueId));
        if (filter.OfficialId is { } officialId) drafts = drafts.Where(draft => draft.OfficialIds.Contains(officialId));
        if (filter.From is { } from) drafts = drafts.Where(draft => draft.EventOn >= from);
        if (filter.To is { } to) drafts = drafts.Where(draft => draft.EventOn <= to);
        return drafts;
    }
}
