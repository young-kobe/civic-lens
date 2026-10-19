using CivicLens.Application.Collection;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Collection.Jobs;
using CivicLens.Infrastructure.Collection.Jobs.Persistence;
using CivicLens.Infrastructure.Statements.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CivicLens.Infrastructure.Statements;

internal sealed class AdmittingRevisionReader
{
    private CollectionConfigurationRevision? lastRevision;

    public async Task<CollectionConfigurationRevision?> ReadAsync(CollectionAttemptDbContext db, string candidateId,
        CancellationToken cancellationToken)
    {
        var revisionId = (await ReadRevisionIdsAsync(db, [candidateId], cancellationToken)).GetValueOrDefault(candidateId);
        if (revisionId is null) return null;
        if (lastRevision?.Id == revisionId) return lastRevision;
        var revisions = await StoredJobDefinition.LoadRevisionsAsync(db, [revisionId], cancellationToken);
        lastRevision = StoredJobDefinition.Find(revisions, revisionId);
        return lastRevision;
    }

    public async Task<Dictionary<string, CollectionConfigurationRevision>> ReadManyAsync(CollectionAttemptDbContext db,
        string[] candidateIds, CancellationToken cancellationToken)
    {
        var revisionIds = await ReadRevisionIdsAsync(db, candidateIds, cancellationToken);
        var revisions = await StoredJobDefinition.LoadRevisionsAsync(db, revisionIds.Values, cancellationToken);
        return revisionIds.ToDictionary(pair => pair.Key, pair => StoredJobDefinition.Find(revisions, pair.Value), StringComparer.Ordinal);
    }

    private static Task<Dictionary<string, string>> ReadRevisionIdsAsync(CollectionAttemptDbContext db, string[] candidateIds,
        CancellationToken cancellationToken) =>
        (from candidate in db.Set<StatementCandidateRow>().AsNoTracking()
         join jobAttempt in db.Set<JobAttemptRow>().AsNoTracking() on candidate.AttemptId equals jobAttempt.AttemptId
         join job in db.Set<JobRow>().AsNoTracking() on jobAttempt.JobId equals job.JobId
         where candidateIds.Contains(candidate.CandidateId) && job.ConfigurationRevisionId != null
         select new { candidate.CandidateId, RevisionId = job.ConfigurationRevisionId! })
        .ToDictionaryAsync(row => row.CandidateId, row => row.RevisionId, StringComparer.Ordinal, cancellationToken);
}
