using CivicLens.Application.Collection;
using CivicLens.Application.Collection.FederalRegister;
using CivicLens.Infrastructure.Collection.Jobs.Persistence;
using CivicLens.Infrastructure.ExecutiveActions.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CivicLens.Infrastructure.Collection.Jobs;

public sealed partial class PostgresCollectionJobStore : IFederalRegisterTextBackfillStore
{
    public async Task<IReadOnlyList<FederalRegisterTextTarget>> ListNewestOriginalsWithoutTextAsync(ConfiguredCollectionSource textSource,
        int limit, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(textSource);
        var sourceId = textSource.FederalRegisterDocumentsSourceId;
        var prefix = FederalRegisterJobKeys.Text(textSource.SourceId, "");
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var rows = await db.Set<ExecutiveActionDocumentRow>().AsNoTracking()
            .Where(row => row.SourceId == sourceId && row.NoticeUrl == null && row.RevisionNumber != null)
            .Where(row => !db.Set<ExecutiveActionTextRow>().Any(text => text.SourceId == row.SourceId && text.DocumentNumber == row.DocumentNumber))
            .Where(row => !db.Set<JobRow>().Any(job => job.IdempotencyKey == prefix + row.DocumentNumber))
            .OrderByDescending(row => row.PublishedOn).ThenBy(row => row.DocumentNumber)
            .Select(row => new { row.DocumentNumber, row.PublishedOn }).Take(limit).ToListAsync(cancellationToken);
        return [.. rows.Select(row => new FederalRegisterTextTarget(row.DocumentNumber, row.PublishedOn))];
    }

    public Task<bool> AdmitTextJobAsync(ConfiguredCollectionSource textSource, FederalRegisterTextTarget target, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(textSource);
        ArgumentNullException.ThrowIfNull(target);
        return TransactionAsync(async (db, now) =>
        {
            var batch = textSource.CreateFederalRegisterTextBatch(target.DocumentNumber, target.PublishedOn, Today(now));
            var (result, _) = await AdmitApiJobsAsync(db, batch, ApiJobSaveBatchSize, now, cancellationToken);
            return result.AdmittedCount > 0;
        }, cancellationToken);
    }
}
