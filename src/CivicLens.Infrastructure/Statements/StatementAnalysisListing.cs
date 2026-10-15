using System.Linq.Expressions;
using CivicLens.Application.Statements;
using CivicLens.Infrastructure.Analysis;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Documents;
using CivicLens.Infrastructure.Statements.Persistence;
using Microsoft.EntityFrameworkCore;
using State = CivicLens.Application.Analysis.AnalysisState;

namespace CivicLens.Infrastructure.Statements;

internal sealed record StatementQueueEntry(StatementAnalysisRow Row, string SourceId, string Url, string? LatestRunId,
    string? LatestIssueRevisionId);

public static class StatementAnalysisListing
{
    public const string Table = "statement_analysis";

    internal static async Task<IReadOnlyList<StatementQueueEntry>> ListAsync(IDbContextFactory<CollectionAttemptDbContext> contextFactory,
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
        var latest = await AnalysisRunLookup.Latest<StatementAnalysisRunRow>(db, task, nameof(StatementAnalysisRunRow.CandidateId), ids)
            .Select(run => new { run.CandidateId, run.RunId, run.IssueRevisionId }).ToDictionaryAsync(run => run.CandidateId, cancellationToken);
        return [.. rows.Select(row => new StatementQueueEntry(row.Entry, row.SourceId, row.Url,
            latest.GetValueOrDefault(row.Entry.CandidateId)?.RunId, latest.GetValueOrDefault(row.Entry.CandidateId)?.IssueRevisionId))];
    }

    internal static async Task<StatementArticle?> ReadArticleAsync(CollectionAttemptDbContext db, string candidateId,
        CancellationToken cancellationToken)
    {
        var extractionId = await db.Set<StatementCandidateRow>().AsNoTracking().Where(row => row.CandidateId == candidateId)
            .Select(row => row.ExtractionId).SingleOrDefaultAsync(cancellationToken);
        if (extractionId is null) return null;
        var extraction = await PostgresDocumentExtractionStore.ReadAsync(db, extractionId, cancellationToken);
        return extraction is null ? null : new(candidateId, extraction);
    }
}
