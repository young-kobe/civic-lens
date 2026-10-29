using CivicLens.Application.Collection;
using CivicLens.Application.Collection.Discovery;
using CivicLens.Application.Collection.FederalRegister;
using CivicLens.Infrastructure.ExecutiveActions;
using CivicLens.Infrastructure.ExecutiveActions.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CivicLens.Infrastructure.Collection.Jobs;

public sealed partial class PostgresCollectionJobStore : IFederalRegisterAdmissionStore
{
    public Task<DiscoveryAdmissionResult> AdmitListPageAsync(ConfiguredCollectionSource source, FederalRegisterListPage page,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(page);
        return TransactionAsync(async (db, now) =>
        {
            var batch = source.CreateFederalRegisterListBatch(page, Today(now));
            var (result, _) = await AdmitApiJobsAsync(db, batch, ApiJobSaveBatchSize, now, cancellationToken);
            if (result.DeferredCount > 0)
                throw new InvalidDataException($"Source '{source.SourceId}' admission policy cannot hold a full Federal Register list.");
            return result;
        }, cancellationToken);
    }

    public Task<DiscoveryAdmissionResult> AdmitDocumentAsync(ConfiguredCollectionSource source, string documentJobId,
        FederalRegisterEvidence evidence, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentException.ThrowIfNullOrWhiteSpace(documentJobId);
        return TransactionAsync(async (db, now) =>
        {
            var document = evidence.Document;
            if (await db.Set<ExecutiveActionDocumentRow>().AnyAsync(row => row.SourceId == source.SourceId &&
                    row.DocumentNumber == document.DocumentNumber, cancellationToken))
                return NoAdmission with { DuplicateCount = 1 };
            await EnsureExtractionIsOfJobAsync(db, documentJobId, evidence.ExtractionId, cancellationToken);
            var revision = await ExecutiveActionRows.AppendCodeApprovedAsync(db, source.SourceId, evidence, now, cancellationToken);
            db.Add(new ExecutiveActionDocumentRow
            {
                SourceId = source.SourceId,
                DocumentNumber = document.DocumentNumber,
                RecordId = revision.RecordId,
                RevisionNumber = revision.RevisionNumber,
                PublishedOn = document.PublishedOn,
                DocumentJobId = documentJobId
            });
            return NoAdmission;
        }, cancellationToken);
    }

    private static Task<DateOnly?> ReadNewestPublishedOnAsync(CollectionAttemptDbContext db, string sourceId,
        CancellationToken cancellationToken) =>
        db.Set<ExecutiveActionDocumentRow>().Where(row => row.SourceId == sourceId)
            .Select(row => (DateOnly?)row.PublishedOn).MaxAsync(cancellationToken);
}
