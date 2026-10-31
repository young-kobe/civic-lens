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
        var revisionId = await (from candidate in db.Set<StatementCandidateRow>().AsNoTracking()
                                join attempt in AttemptRevisionIds(db) on candidate.AttemptId equals attempt.AttemptId
                                where candidate.CandidateId == candidateId
                                select attempt.RevisionId).FirstOrDefaultAsync(cancellationToken);
        return await LoadAsync(db, revisionId, cancellationToken);
    }

    public async Task<CollectionConfigurationRevision?> ReadForAttemptAsync(CollectionAttemptDbContext db, string attemptId,
        CancellationToken cancellationToken)
    {
        var revisionId = await AttemptRevisionIds(db).Where(row => row.AttemptId == attemptId)
            .Select(row => row.RevisionId).FirstOrDefaultAsync(cancellationToken);
        return await LoadAsync(db, revisionId, cancellationToken);
    }

    private async Task<CollectionConfigurationRevision?> LoadAsync(CollectionAttemptDbContext db, string? revisionId,
        CancellationToken cancellationToken)
    {
        if (revisionId is null) return null;
        var cached = lastRevision;
        if (cached?.Id == revisionId) return cached;
        var revisions = await StoredJobDefinition.LoadRevisionsAsync(db, [revisionId], cancellationToken);
        return lastRevision = StoredJobDefinition.Find(revisions, revisionId);
    }

    private static IQueryable<AttemptRevision> AttemptRevisionIds(CollectionAttemptDbContext db) =>
        from jobAttempt in db.Set<JobAttemptRow>().AsNoTracking()
        join job in db.Set<JobRow>().AsNoTracking() on jobAttempt.JobId equals job.JobId
        where job.ConfigurationRevisionId != null
        select new AttemptRevision { AttemptId = jobAttempt.AttemptId, RevisionId = job.ConfigurationRevisionId! };

    private sealed class AttemptRevision
    {
        public string AttemptId { get; init; } = "";
        public string RevisionId { get; init; } = "";
    }
}
