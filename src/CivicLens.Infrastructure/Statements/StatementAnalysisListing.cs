using System.Linq.Expressions;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Statements.Persistence;
using Microsoft.EntityFrameworkCore;
using State = CivicLens.Application.Analysis.AnalysisState;

namespace CivicLens.Infrastructure.Statements;

internal sealed record StatementQueueEntry(StatementAnalysisRow Row, string SourceId, string Url, string? LatestRunId,
    string? LatestIssueRevisionId);

internal static class StatementAnalysisListing
{
    public static async Task<IReadOnlyList<StatementQueueEntry>> ListAsync(IDbContextFactory<CollectionAttemptDbContext> contextFactory,
        string task, Expression<Func<StatementAnalysisRow, bool>>? filter, int limit, CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var queue = db.Set<StatementAnalysisRow>().AsNoTracking().Where(row => row.Task == task);
        if (filter is not null) queue = queue.Where(filter);
        var rows = await (from entry in queue
                          join candidate in db.Set<StatementCandidateRow>().AsNoTracking() on entry.CandidateId equals candidate.CandidateId
                          orderby entry.CreatedAtUtcTicks, entry.CandidateId
                          select new { Entry = entry, candidate.SourceId, candidate.Url }).Take(limit).ToArrayAsync(cancellationToken);
        var ids = rows.Select(row => row.Entry.CandidateId).ToArray();
        var runs = await db.Set<StatementAnalysisRunRow>().AsNoTracking()
            .Where(run => run.Task == task && ids.Contains(run.CandidateId))
            .Select(run => new { run.CandidateId, run.RunId, run.IssueRevisionId, run.StartedAtUtcTicks }).ToArrayAsync(cancellationToken);
        var latest = runs.GroupBy(run => run.CandidateId).ToDictionary(group => group.Key,
            group => group.OrderByDescending(run => run.StartedAtUtcTicks).First());
        return [.. rows.Select(row => new StatementQueueEntry(row.Entry, row.SourceId, row.Url,
            latest.GetValueOrDefault(row.Entry.CandidateId)?.RunId, latest.GetValueOrDefault(row.Entry.CandidateId)?.IssueRevisionId))];
    }

    public static async Task<IReadOnlyDictionary<TView, int>> CountAsync<TView>(IDbContextFactory<CollectionAttemptDbContext> contextFactory,
        string task, Func<State, string?, TView> viewOf, CancellationToken cancellationToken) where TView : struct, Enum
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var groups = await db.Set<StatementAnalysisRow>().AsNoTracking().Where(row => row.Task == task)
            .GroupBy(row => new { row.Status, row.ErrorCode }).Select(group => new { group.Key.Status, group.Key.ErrorCode, Count = group.Count() })
            .ToArrayAsync(cancellationToken);
        var counts = Enum.GetValues<TView>().ToDictionary(view => view, _ => 0);
        foreach (var group in groups) counts[viewOf(group.Status, group.ErrorCode)] += group.Count;
        return counts;
    }
}
