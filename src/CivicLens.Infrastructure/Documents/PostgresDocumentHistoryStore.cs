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
            .Take(maximumObservations + 1)
            .ToListAsync(cancellationToken);
        if (rows.Count > maximumObservations)
            throw new DocumentHistoryLimitException();
        // An accepted history contains every row. Sort only that bounded set using .NET ordinal ordering.
        rows.Sort((left, right) =>
        {
            var timeOrder = left.ObservedAtUtcTicks.CompareTo(right.ObservedAtUtcTicks);
            return timeOrder != 0 ? timeOrder : string.CompareOrdinal(left.AttemptId, right.AttemptId);
        });

        var attempts = await PostgresCollectionAttemptStore.ReadEvidenceAsync(db, rows, cancellationToken);
        var extractionAttemptIds = attempts.Select(attempt => ExtractionAttemptId(attempt, sourceId, requestedUrl))
            .Where(id => id is not null).Distinct(StringComparer.Ordinal).ToArray();

        var extractionMetadata = extractionAttemptIds.Length == 0 ? [] : await db.Set<DocumentExtractionRow>().AsNoTracking()
            .Where(row => extractionAttemptIds.Contains(row.AttemptId))
            .Select(row => new { row.ExtractionId, row.AttemptId, TextLength = row.Text.Length })
            .Take(MaximumExtractionRows + 1)
            .ToListAsync(cancellationToken);
        if (extractionMetadata.Count > MaximumExtractionRows)
            throw new DocumentHistoryLimitException();
        var metadataByAttempt = extractionMetadata.ToLookup(row => row.AttemptId, StringComparer.Ordinal);
        long materializedLength = 0;
        foreach (var attempt in attempts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var id = ExtractionAttemptId(attempt, sourceId, requestedUrl);
            if (id is null) continue;
            foreach (var row in metadataByAttempt[id])
            {
                materializedLength += 2L * row.TextLength;
                if (materializedLength > MaximumUtf16TextLength)
                    throw new DocumentHistoryLimitException();
            }
        }

        var extractionIds = extractionMetadata.Select(metadata => metadata.ExtractionId).ToArray();
        var extractionRows = await db.Set<DocumentExtractionRow>().AsNoTracking()
            .Where(row => extractionIds.Contains(row.ExtractionId)).ToListAsync(cancellationToken);
        var capturedAttempts = attempts.SelectMany(attempt => new[]
            { attempt.AttemptResult as CapturedAttemptResult, attempt.PriorCapturedAttempt })
            .OfType<CapturedAttemptResult>().DistinctBy(attempt => attempt.AttemptId)
            .ToDictionary(attempt => attempt.AttemptId, StringComparer.Ordinal);
        var extractionCache = new Dictionary<string, CivicLens.Core.Documents.DocumentExtraction>(StringComparer.Ordinal);
        foreach (var row in extractionRows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!capturedAttempts.TryGetValue(row.AttemptId, out var captured))
                throw new InvalidOperationException("Stored extraction has no captured source attempt.");
            extractionCache.Add(row.ExtractionId, PostgresDocumentExtractionStore.FromRow(row, captured));
        }
        var extractionsByAttempt = extractionRows.ToLookup(row => row.AttemptId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Select(row => extractionCache[row.ExtractionId])
                .OrderBy(extraction => extraction.ParserVersion, StringComparer.Ordinal)
                .ThenBy(extraction => extraction.NormalizationVersion, StringComparer.Ordinal)
                .ThenBy(extraction => extraction.Profile?.RevisionId, StringComparer.Ordinal).ToImmutableArray(),
                StringComparer.Ordinal);

        var result = attempts.Select(attempt =>
        {
            var id = ExtractionAttemptId(attempt, sourceId, requestedUrl);
            var extractions = id is not null && extractionsByAttempt.TryGetValue(id, out var retained) ? retained : [];
            return new DocumentHistoryObservation(attempt, extractions);
        }).ToArray();
        cancellationToken.ThrowIfCancellationRequested();
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    private static string? ExtractionAttemptId(CivicLens.Application.Collection.StoredCollectionAttempt attempt,
        string sourceId, string requestedUrl) => attempt.AttemptResult switch
        {
            CapturedAttemptResult captured => captured.AttemptId,
            NotModifiedAttemptResult when attempt.PriorCapturedAttempt is { } prior &&
                prior.SourceId == sourceId && prior.RequestedUrl == requestedUrl => prior.AttemptId,
            _ => null
        };
}
