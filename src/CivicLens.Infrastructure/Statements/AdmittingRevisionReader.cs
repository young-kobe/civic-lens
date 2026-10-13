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
                                join jobAttempt in db.Set<JobAttemptRow>().AsNoTracking() on candidate.AttemptId equals jobAttempt.AttemptId
                                join job in db.Set<JobRow>().AsNoTracking() on jobAttempt.JobId equals job.JobId
                                where candidate.CandidateId == candidateId
                                select job.ConfigurationRevisionId).SingleOrDefaultAsync(cancellationToken);
        if (revisionId is null) return null;
        if (lastRevision?.Id == revisionId) return lastRevision;
        var revisions = await StoredJobDefinition.LoadRevisionsAsync(db, [revisionId], cancellationToken);
        lastRevision = StoredJobDefinition.Find(revisions, revisionId);
        return lastRevision;
    }
}
