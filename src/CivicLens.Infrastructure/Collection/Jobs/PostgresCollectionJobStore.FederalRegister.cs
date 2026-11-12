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
            var revision = await executiveActions.AdmitAsync(db, source.SourceId, documentJobId, evidence, now, cancellationToken);
            if (document.CorrectionOf is not { } original)
                return await AdmitTextJobAsync(db, source, document, now, cancellationToken);
            return revision is null
                ? await AdmitOriginalJobAsync(db, source, original, document.PublishedOn, now, cancellationToken)
                : NoAdmission;
        }, cancellationToken);
    }

    /// <summary>An original document with a pinned body URL gets one text job in the transaction that records it. A correction gets none.</summary>
    private static async Task<DiscoveryAdmissionResult> AdmitTextJobAsync(CollectionAttemptDbContext db, ConfiguredCollectionSource source,
        FederalRegisterDocument document, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (source.EnabledFederalRegisterTextSource() is not { } textSource || document.BodyHtmlUrl is not { } bodyUrl) return NoAdmission;
        var batch = textSource.CreateFederalRegisterTextBatch(document.DocumentNumber, bodyUrl, document.PublishedOn, Today(now));
        var (result, _) = await AdmitApiJobsAsync(db, batch, ApiJobSaveBatchSize, now, cancellationToken);
        if (result.DeferredCount > 0)
            throw new InvalidDataException($"Source '{textSource.SourceId}' admission policy cannot hold the text job of a document.");
        return result;
    }

    public Task AdmitBodyUrlAsync(ConfiguredCollectionSource source, string documentNumber, string? bodyUrl, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(documentNumber);
        return TransactionAsync(async (db, _) => await db.Set<ExecutiveActionDocumentRow>()
            .Where(row => row.SourceId == source.SourceId && row.DocumentNumber == documentNumber && row.BodyHtmlUrl == null)
            .ExecuteUpdateAsync(set => set.SetProperty(row => row.BodyHtmlUrl, bodyUrl), cancellationToken), cancellationToken);
    }

    public Task AdmitTextAsync(ConfiguredCollectionSource textSource, string textJobId, FederalRegisterTextEvidence evidence,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(textSource);
        ArgumentException.ThrowIfNullOrWhiteSpace(textJobId);
        ArgumentNullException.ThrowIfNull(evidence);
        return TransactionAsync(async (db, _) =>
        {
            await EnsureExtractionIsOfJobAsync(db, textJobId, evidence.ExtractionId, cancellationToken);
            var sourceId = textSource.FederalRegisterDocumentsSourceId;
            var document = await db.Set<ExecutiveActionDocumentRow>().AsNoTracking().SingleOrDefaultAsync(row =>
                row.SourceId == sourceId && row.DocumentNumber == evidence.DocumentNumber, cancellationToken)
                ?? throw new InvalidDataException("Text job names a document that is not recorded.");
            if (document.NoticeUrl is not null || document.BodyHtmlUrl != evidence.BodyUrl)
                throw new InvalidDataException("Text job names a correction notice or another URL than the pinned body URL of the document.");
            if (!await db.Set<ExecutiveActionTextRow>().AnyAsync(row => row.SourceId == sourceId &&
                    row.DocumentNumber == evidence.DocumentNumber, cancellationToken))
                db.Add(new ExecutiveActionTextRow
                {
                    SourceId = sourceId,
                    DocumentNumber = evidence.DocumentNumber,
                    ExtractionId = evidence.ExtractionId,
                    CharsTotal = evidence.Reading.CharsTotal,
                    CharsRead = evidence.Reading.CharsRead
                });
            return 0;
        }, cancellationToken);
    }

    private static async Task<DiscoveryAdmissionResult> AdmitOriginalJobAsync(CollectionAttemptDbContext db, ConfiguredCollectionSource source,
        string originalNumber, DateOnly correctedOn, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var page = new FederalRegisterListPage([new FederalRegisterListItem(originalNumber, correctedOn)]);
        var (result, _) = await AdmitApiJobsAsync(db, source.CreateFederalRegisterListBatch(page, Today(now)), ApiJobSaveBatchSize, now, cancellationToken);
        if (result.DeferredCount > 0)
            throw new InvalidDataException($"Source '{source.SourceId}' admission policy cannot hold the job of a corrected document.");
        return result;
    }

    private static Task<DateOnly?> ReadNewestPublishedOnAsync(CollectionAttemptDbContext db, string sourceId,
        CancellationToken cancellationToken) =>
        db.Set<ExecutiveActionDocumentRow>().Where(row => row.SourceId == sourceId)
            .Select(row => (DateOnly?)row.PublishedOn).MaxAsync(cancellationToken);
}
