using System.Collections.Immutable;
using CivicLens.Application.Documents;
using CivicLens.Core.Collection;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Documents.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace CivicLens.Infrastructure.Documents;

/// <summary>Reads bounded, immutable observation history and its persisted extraction revisions.</summary>
public sealed class PostgresDocumentHistoryStore(IDbContextFactory<CollectionAttemptDbContext> contextFactory)
    : IDocumentHistoryStore
{
    public const int MaximumObservations = GetDocumentHistory.MaximumObservations;
    public const int MaximumExtractionRows = 10_000;
    private const long MaximumUtf16TextLength = GetDocumentHistory.MaximumMaterializedTextLength;

    public static PostgresDocumentHistoryStore FromConnectionString(string connectionString)
    {
        _ = PostgresCollectionAttemptStore.FromConnectionString(connectionString);
        var options = new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString).Options;
        return new PostgresDocumentHistoryStore(new PooledDbContextFactory<CollectionAttemptDbContext>(options));
    }

    public async Task<IReadOnlyList<DocumentHistoryObservation>> GetAsync(string sourceId, string requestedUrl,
        int maximumObservations, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestedUrl);
        if (maximumObservations is < 1 or > MaximumObservations)
            throw new ArgumentOutOfRangeException(nameof(maximumObservations));

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead,
            cancellationToken);
        var rows = await db.Attempts.AsNoTracking()
            .Where(row => row.SourceId == sourceId && row.RequestedUrl == requestedUrl)
            .OrderBy(row => row.ObservedAtUtcTicks)
            .ThenBy(row => EF.Functions.Collate(row.AttemptId, "C"))
            .Take(maximumObservations + 1)
            .ToListAsync(cancellationToken);
        if (rows.Count > maximumObservations)
            throw new DocumentHistoryLimitException();
        // PostgreSQL's C collation orders Unicode scalars; .NET ordinal comparison orders UTF-16 units.
        rows.Sort((left, right) =>
        {
            var timeOrder = left.ObservedAtUtcTicks.CompareTo(right.ObservedAtUtcTicks);
            return timeOrder != 0 ? timeOrder : string.CompareOrdinal(left.AttemptId, right.AttemptId);
        });

        var attempts = new List<CivicLens.Application.Collection.StoredCollectionAttempt>(rows.Count);
        foreach (var row in rows)
        {
            var attempt = await PostgresCollectionAttemptStore.ReadStoredAttemptAsync(db, row, cancellationToken,
                includeDiscovery: false);
            attempts.Add(attempt);
        }

        var extractionAttemptIds = attempts.Select(attempt => attempt.AttemptResult switch
        {
            CapturedAttemptResult captured => captured.AttemptId,
            NotModifiedAttemptResult when attempt.PriorCapturedAttempt is { } prior &&
                prior.SourceId == sourceId && prior.RequestedUrl == requestedUrl => prior.AttemptId,
            _ => null
        }).Where(id => id is not null).Distinct(StringComparer.Ordinal).ToArray();

        var extractionMetadata = extractionAttemptIds.Length == 0 ? [] : await db.Set<DocumentExtractionRow>().AsNoTracking()
            .Where(row => extractionAttemptIds.Contains(row.AttemptId))
            .Select(row => new { row.ExtractionId, row.AttemptId, TextLength = row.Text.Length })
            .Take(MaximumExtractionRows + 1)
            .ToListAsync(cancellationToken);
        if (extractionMetadata.Count > MaximumExtractionRows)
            throw new DocumentHistoryLimitException();
        var extractionLengths = extractionMetadata.ToDictionary(row => row.ExtractionId,
            row => row.TextLength, StringComparer.Ordinal);
        long materializedLength = 0;
        foreach (var attempt in attempts)
        {
            var id = attempt.AttemptResult is CapturedAttemptResult captured ? captured.AttemptId :
                attempt.AttemptResult is NotModifiedAttemptResult && attempt.PriorCapturedAttempt is { } prior &&
                prior.SourceId == sourceId && prior.RequestedUrl == requestedUrl ? prior.AttemptId : null;
            if (id is null) continue;
            foreach (var row in extractionMetadata.Where(candidate => candidate.AttemptId == id))
            {
                materializedLength += 2L * extractionLengths[row.ExtractionId];
                if (materializedLength > MaximumUtf16TextLength)
                    throw new DocumentHistoryLimitException();
            }
        }

        var extractionIds = extractionMetadata.Select(metadata => metadata.ExtractionId).ToArray();
        var extractionCache = new Dictionary<string, CivicLens.Core.Documents.DocumentExtraction>(StringComparer.Ordinal);
        foreach (var extractionId in extractionIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var extraction = await PostgresDocumentExtractionStore.ReadAsync(db, extractionId, cancellationToken)
                ?? throw new InvalidOperationException("A retained extraction disappeared during its read.");
            extractionCache.Add(extractionId, extraction);
        }

        var result = attempts.Select(attempt =>
        {
            var id = attempt.AttemptResult is CapturedAttemptResult captured ? captured.AttemptId :
                attempt.AttemptResult is NotModifiedAttemptResult && attempt.PriorCapturedAttempt is { } prior &&
                prior.SourceId == sourceId && prior.RequestedUrl == requestedUrl ? prior.AttemptId : null;
            var extractions = id is null ? [] : extractionMetadata.Where(row => row.AttemptId == id)
                .Select(row => extractionCache[row.ExtractionId])
                .OrderBy(extraction => extraction.ParserVersion, StringComparer.Ordinal)
                .ThenBy(extraction => extraction.NormalizationVersion, StringComparer.Ordinal)
                .ThenBy(extraction => extraction.Profile?.RevisionId, StringComparer.Ordinal)
                .ToImmutableArray();
            return new DocumentHistoryObservation(attempt, extractions);
        }).ToArray();
        cancellationToken.ThrowIfCancellationRequested();
        await transaction.CommitAsync(cancellationToken);
        return result;
    }
}
