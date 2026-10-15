using System.Data;
using System.Text.Json;
using CivicLens.Application.Documents;
using CivicLens.Collection.Contracts;
using CivicLens.Core.Documents;
using CivicLens.Core;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Documents.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace CivicLens.Infrastructure.Documents;

public sealed class PostgresDocumentComparisonStore(IDbContextFactory<CollectionAttemptDbContext> contextFactory)
    : IDocumentComparisonStore
{
    public static PostgresDocumentComparisonStore FromConnectionString(string connectionString)
    {
        _ = PostgresCollectionAttemptStore.FromConnectionString(connectionString);
        var options = new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString).Options;
        return new PostgresDocumentComparisonStore(new PooledDbContextFactory<CollectionAttemptDbContext>(options));
    }

    public async Task<DocumentComparison?> GetAsync(string comparisonId, CancellationToken cancellationToken)
    {
        ValidateId(comparisonId);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var row = await db.Set<DocumentComparisonRow>().AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.ComparisonId == comparisonId, cancellationToken);
        if (row is null) return null;
        var result = await ReconstructAsync(db, row.BeforeExtractionId, row.AfterExtractionId, cancellationToken);
        ValidateRetained(row, result);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    public async Task<DocumentComparison> CompareAndSaveAsync(string beforeExtractionId, string afterExtractionId,
        CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var before = await ReadRequestedAsync(db, beforeExtractionId, cancellationToken);
        var after = await ReadRequestedAsync(db, afterExtractionId, cancellationToken);
        var comparison = DocumentComparison.Create(before, after, cancellationToken);
        if (comparison.Status != DocumentComparisonStatus.Complete) return comparison;

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var resultJson = Serialize(comparison);
        var inserted = await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO document_comparisons (comparison_id, before_extraction_id, after_extraction_id,
                algorithm_version, settings_version, result_json, has_changes)
            VALUES ({comparison.ComparisonId}, {comparison.BeforeExtractionId}, {comparison.AfterExtractionId},
                {comparison.AlgorithmVersion}, {comparison.SettingsVersion}, {resultJson}, {comparison.HasChanges()})
            ON CONFLICT (comparison_id) DO NOTHING
            """, cancellationToken);
        if (inserted == 0)
        {
            var winner = await db.Set<DocumentComparisonRow>().AsNoTracking()
                .SingleAsync(row => row.ComparisonId == comparison.ComparisonId, cancellationToken);
            ValidateRetained(winner, comparison);
            return comparison;
        }
        if (comparison.HasChanges())
            await EnqueueDraftAnalysisAsync(db, comparison.ComparisonId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return comparison;
    }

    private static async Task EnqueueDraftAnalysisAsync(CollectionAttemptDbContext db, string comparisonId,
        CancellationToken cancellationToken)
    {
        var now = (await DatabaseClock.ReadAsync(db, cancellationToken)).Ticks;
        _ = await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO document_change_analysis (comparison_id, task, status, fence, attempts,
                reserved_tokens, created_at_utc_ticks)
            VALUES ({comparisonId}, 'draft', 'Pending', 0, 0, 0, {now})
            ON CONFLICT (comparison_id, task) DO NOTHING
            """, cancellationToken);
    }

    private static async Task<DocumentExtraction> ReadRequestedAsync(CollectionAttemptDbContext db, string extractionId,
        CancellationToken cancellationToken)
    {
        PostgresDocumentExtractionStore.ValidateId(extractionId);
        return await PostgresDocumentExtractionStore.ReadAsync(db, extractionId, cancellationToken)
            ?? throw new ArgumentException("Extraction was not found.", nameof(extractionId));
    }

    private static async Task<DocumentComparison> ReconstructAsync(CollectionAttemptDbContext db, string beforeId,
        string afterId, CancellationToken cancellationToken)
    {
        var before = await PostgresDocumentExtractionStore.ReadAsync(db, beforeId, cancellationToken)
            ?? throw new InvalidOperationException("Comparison's before extraction is missing.");
        var after = await PostgresDocumentExtractionStore.ReadAsync(db, afterId, cancellationToken)
            ?? throw new InvalidOperationException("Comparison's after extraction is missing.");
        return DocumentComparison.Create(before, after, cancellationToken);
    }

    private static void ValidateRetained(DocumentComparisonRow row, DocumentComparison result)
    {
        if (row.ComparisonId != result.ComparisonId || row.BeforeExtractionId != result.BeforeExtractionId ||
            row.AfterExtractionId != result.AfterExtractionId || row.AlgorithmVersion != result.AlgorithmVersion ||
            row.SettingsVersion != result.SettingsVersion || row.ResultJson != Serialize(result))
            throw new InvalidOperationException("Stored comparison identity, processing version, or evidence is invalid.");
    }

    private static string Serialize(DocumentComparison comparison) =>
        JsonSerializer.Serialize(comparison, CollectionProtocol.JsonOptions);

    private static void ValidateId(string id)
    {
        if (!LowercaseHex.Is(id, 64))
            throw new ArgumentException("Comparison ID must be a lowercase SHA-256 value.", nameof(id));
    }
}
