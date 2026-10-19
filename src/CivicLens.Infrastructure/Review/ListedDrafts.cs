using System.Collections.Immutable;
using CivicLens.Application.Paging;
using CivicLens.Application.Review;
using CivicLens.Core.Review;
using CivicLens.Infrastructure.Analysis.Persistence;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Review.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CivicLens.Infrastructure.Review;

/// <summary>The list read of statement, action, and vote drafts. The analysis row of the draft task owns the tier of actions and votes.</summary>
internal static class ListedDrafts
{
    public static Task<KeysetSlice<RankedDraft<TDraftRow>>> PageByTierAsync<TDraftRow, TAnalysisRow>(CollectionAttemptDbContext db,
        IQueryable<TDraftRow> drafts, string analysisTask, string recordIdProperty, PageCursor? cursor, int limit, DraftListFilter filter,
        CancellationToken cancellationToken)
        where TDraftRow : class, IListedDraftRow
        where TAnalysisRow : class =>
        PageAsync(Rank<TDraftRow, TAnalysisRow>(db, Filter(drafts, filter), analysisTask), recordIdProperty, cursor, limit, cancellationToken);

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

    public static ImmutableDictionary<string, int> PrioritiesOf<TDraftRow>(IEnumerable<RankedDraft<TDraftRow>> items)
        where TDraftRow : class, IListedDraftRow =>
        items.ToImmutableDictionary(item => item.Draft.RecordId, item => item.Priority, StringComparer.Ordinal);

    private static IQueryable<TDraftRow> Filter<TDraftRow>(IQueryable<TDraftRow> drafts, DraftListFilter filter)
        where TDraftRow : class, IListedDraftRow
    {
        if (filter.IssueId is { } issueId) drafts = drafts.Where(draft => draft.IssueIds.Contains(issueId));
        if (filter.OfficialId is { } officialId) drafts = drafts.Where(draft => draft.OfficialIds.Contains(officialId));
        if (filter.From is { } from) drafts = drafts.Where(draft => draft.EventOn >= from);
        if (filter.To is { } to) drafts = drafts.Where(draft => draft.EventOn <= to);
        return drafts;
    }

    private static IQueryable<RankedDraft<TDraftRow>> Rank<TDraftRow, TAnalysisRow>(CollectionAttemptDbContext db,
        IQueryable<TDraftRow> drafts, string analysisTask)
        where TDraftRow : class, IListedDraftRow
        where TAnalysisRow : class
    {
        var analyses = db.Set<TAnalysisRow>().AsNoTracking().Where(analysis => EF.Property<string>(analysis, nameof(IAnalysisRow.Task)) == analysisTask);
        return from draft in drafts
               join analysis in analyses on draft.CandidateId equals EF.Property<string>(analysis, nameof(IListedDraftRow.CandidateId)) into matches
               from match in matches.DefaultIfEmpty()
               select new RankedDraft<TDraftRow>
               {
                   Draft = draft,
                   Priority = EF.Property<int?>(match, nameof(IAnalysisRow.Priority)) ?? ReviewPriority.Lowest
               };
    }
}
