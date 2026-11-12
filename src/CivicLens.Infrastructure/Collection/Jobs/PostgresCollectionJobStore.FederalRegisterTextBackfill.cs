using CivicLens.Application.Collection;
using CivicLens.Application.Collection.FederalRegister;
using CivicLens.Application.Collection.Jobs;
using CivicLens.Infrastructure.Collection.Jobs.Persistence;
using CivicLens.Infrastructure.ExecutiveActions.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CivicLens.Infrastructure.Collection.Jobs;

public sealed partial class PostgresCollectionJobStore : IFederalRegisterTextBackfillStore
{
    public async Task<int> CountPendingJobsAsync(ConfiguredCollectionSource textSource, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(textSource);
        string[] sourceIds = [textSource.FederalRegisterDocumentsSourceId, textSource.SourceId];
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Set<JobRow>().CountAsync(row => sourceIds.Contains(row.SourceId) && (row.State == CollectionJobState.Pending ||
            row.State == CollectionJobState.Running || row.State == CollectionJobState.WaitingToRetry), cancellationToken);
    }

    public async Task<FederalRegisterTextListing> ListNewestOriginalsWithoutTextAsync(ConfiguredCollectionSource textSource,
        int limit, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(textSource);
        var sourceId = textSource.FederalRegisterDocumentsSourceId;
        var prefix = FederalRegisterJobKeys.Text(textSource.SourceId, "");
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var originals = db.Set<ExecutiveActionDocumentRow>().AsNoTracking()
            .Where(row => row.SourceId == sourceId && row.NoticeUrl == null && row.RevisionNumber != null)
            .Where(row => !db.Set<ExecutiveActionTextRow>().Any(text => text.SourceId == row.SourceId && text.DocumentNumber == row.DocumentNumber))
            .Where(row => !db.Set<JobRow>().Any(job => job.IdempotencyKey == prefix + row.DocumentNumber));
        var rows = await originals.Where(row => row.BodyHtmlUrl != null)
            .OrderByDescending(row => row.PublishedOn).ThenBy(row => row.DocumentNumber)
            .Select(row => new { row.DocumentNumber, row.BodyHtmlUrl, row.PublishedOn }).Take(limit).ToListAsync(cancellationToken);
        var documentPrefix = FederalRegisterJobKeys.Document(sourceId, "");
        var unpinned = await originals.Where(row => row.BodyHtmlUrl == null)
            .Where(row => !db.Set<JobRow>().Any(job => job.IdempotencyKey == documentPrefix + row.DocumentNumber + FederalRegisterJobKeys.BodyUrlSuffix))
            .OrderByDescending(row => row.PublishedOn).ThenBy(row => row.DocumentNumber)
            .Select(row => new FederalRegisterBodyUrlTarget(row.DocumentNumber, row.PublishedOn)).Take(limit).ToListAsync(cancellationToken);
        return new([.. rows.Select(row => new FederalRegisterTextTarget(row.DocumentNumber, row.BodyHtmlUrl!, row.PublishedOn))], unpinned);
    }

    public Task<bool> AdmitBodyUrlJobAsync(ConfiguredCollectionSource textSource, FederalRegisterBodyUrlTarget target,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(textSource);
        ArgumentNullException.ThrowIfNull(target);
        return TransactionAsync(async (db, now) =>
        {
            var batch = textSource.FederalRegisterDocumentsSource().CreateFederalRegisterBodyUrlBatch(target, Today(now));
            var (result, _) = await AdmitApiJobsAsync(db, batch, ApiJobSaveBatchSize, now, cancellationToken);
            return result.AdmittedCount > 0;
        }, cancellationToken);
    }

    public Task<bool> AdmitTextJobAsync(ConfiguredCollectionSource textSource, FederalRegisterTextTarget target, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(textSource);
        ArgumentNullException.ThrowIfNull(target);
        return TransactionAsync(async (db, now) =>
        {
            var batch = textSource.CreateFederalRegisterTextBatch(target.DocumentNumber, target.BodyUrl, target.PublishedOn, Today(now));
            var (result, _) = await AdmitApiJobsAsync(db, batch, ApiJobSaveBatchSize, now, cancellationToken);
            return result.AdmittedCount > 0;
        }, cancellationToken);
    }
}
