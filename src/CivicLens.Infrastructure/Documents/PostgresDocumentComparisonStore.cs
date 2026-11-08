using System.Collections.Immutable;
using System.Data;
using System.Text.Json;
using CivicLens.Application.Documents;
using CivicLens.Collection.Contracts;
using CivicLens.Core;
using CivicLens.Core.Documents;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Documents.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace CivicLens.Infrastructure.Documents;

public sealed class PostgresDocumentComparisonStore(IDbContextFactory<CollectionAttemptDbContext> contextFactory)
    : IDocumentComparisonStore, IDocumentComparisonBackfillStore
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
        await ValidateRetainedAsync(db, row, result, Serialize(result), cancellationToken);
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
                algorithm_version, settings_version, result_json, has_changes, hunk_count)
            VALUES ({comparison.ComparisonId}, {comparison.BeforeExtractionId}, {comparison.AfterExtractionId},
                {comparison.AlgorithmVersion}, {comparison.SettingsVersion}, {resultJson}, {comparison.HasChanges()}, {comparison.Hunks.Length})
            ON CONFLICT (comparison_id) DO NOTHING
            """, cancellationToken);
        if (inserted == 0)
        {
            var winner = await db.Set<DocumentComparisonRow>().FromSqlInterpolated($"""
                SELECT * FROM document_comparisons
                WHERE comparison_id = {comparison.ComparisonId}
                FOR SHARE
                """).AsNoTracking().SingleAsync(cancellationToken);
            await ValidateRetainedAsync(db, winner, comparison, resultJson, cancellationToken);
            return comparison;
        }
        await InsertHunksAsync(db, comparison, cancellationToken);
        if (comparison.HasChanges())
            await EnqueueDraftAnalysisAsync(db, comparison.ComparisonId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return comparison;
    }

    public async Task<DocumentComparisonBackfillBatch> BackfillBatchAsync(string? afterComparisonId, int size,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(size, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(size, 25);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var after = afterComparisonId ?? string.Empty;
        var rows = await db.Set<DocumentComparisonRow>().FromSqlInterpolated($"""
            SELECT * FROM document_comparisons
            WHERE hunk_count IS NULL AND comparison_id > {after}
            ORDER BY comparison_id
            LIMIT {size}
            FOR UPDATE
            """).ToListAsync(cancellationToken);
        var skipped = ImmutableArray.CreateBuilder<string>();
        foreach (var row in rows)
        {
            if (db.Entry(row).State == EntityState.Detached) db.Attach(row);
            await transaction.CreateSavepointAsync("document_comparison_details", cancellationToken);
            try
            {
                await BackfillRowAsync(db, row, cancellationToken);
                await transaction.ReleaseSavepointAsync("document_comparison_details", cancellationToken);
            }
            catch (Exception exception) when (IsInvalidLegacyComparison(exception))
            {
                await transaction.RollbackToSavepointAsync("document_comparison_details", cancellationToken);
                skipped.Add(row.ComparisonId);
                db.ChangeTracker.Clear();
            }
        }
        await transaction.CommitAsync(cancellationToken);
        return new(rows.Count, skipped.ToImmutable(), rows.Count == 0 ? null : rows[^1].ComparisonId);
    }

    private static async Task BackfillRowAsync(CollectionAttemptDbContext db, DocumentComparisonRow row,
        CancellationToken cancellationToken)
    {
        var comparison = await ReconstructAsync(db, row.BeforeExtractionId, row.AfterExtractionId, cancellationToken);
        if (comparison.Status != DocumentComparisonStatus.Complete)
            throw new InvalidDataException("Legacy comparison does not match its retained evidence.");
        var resultJson = Serialize(comparison);
        await ValidateRetainedAsync(db, row, comparison, resultJson, cancellationToken);
        row.HunkCount = comparison.Hunks.Length;
        await InsertHunksAsync(db, comparison, cancellationToken);
        var saved = await db.Set<DocumentComparisonRow>().AsNoTracking()
            .SingleAsync(candidate => candidate.ComparisonId == row.ComparisonId, cancellationToken);
        await ValidateRetainedAsync(db, saved, comparison, resultJson, cancellationToken);
    }

    private static bool IsInvalidLegacyComparison(Exception exception) => exception is
        JsonException or InvalidDataException or InvalidOperationException or ArgumentException;

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

    private static async Task ValidateRetainedAsync(CollectionAttemptDbContext db, DocumentComparisonRow row,
        DocumentComparison result, string resultJson, CancellationToken cancellationToken)
    {
        if (row.ComparisonId != result.ComparisonId || row.BeforeExtractionId != result.BeforeExtractionId ||
            row.AfterExtractionId != result.AfterExtractionId || row.AlgorithmVersion != result.AlgorithmVersion ||
            row.SettingsVersion != result.SettingsVersion || row.HasChanges != result.HasChanges() || row.ResultJson != resultJson)
            throw new InvalidOperationException("Stored comparison identity, processing version, or evidence is invalid.");
        if (row.HunkCount is null)
        {
            if (await db.Set<DocumentComparisonHunkRow>().AsNoTracking()
                    .AnyAsync(candidate => candidate.ComparisonId == row.ComparisonId, cancellationToken))
                throw new InvalidOperationException("Unverified comparison has detail rows.");
            return;
        }
        if (row.HunkCount != result.Hunks.Length)
            throw new InvalidOperationException("Stored comparison hunk count is invalid.");
        var hunks = await db.Set<DocumentComparisonHunkRow>().AsNoTracking()
            .Where(candidate => candidate.ComparisonId == row.ComparisonId).OrderBy(candidate => candidate.Position)
            .Take(result.Hunks.Length + 1)
            .ToListAsync(cancellationToken);
        if (hunks.Count != row.HunkCount)
            throw new InvalidOperationException("Stored comparison hunk count is invalid.");
        var expectedEditCount = result.Hunks.Sum(hunk => hunk.WordEdits.Length);
        var edits = await db.Set<DocumentComparisonWordEditRow>().AsNoTracking()
            .Where(candidate => candidate.ComparisonId == row.ComparisonId)
            .OrderBy(candidate => candidate.HunkPosition).ThenBy(candidate => candidate.Position)
            .Take(expectedEditCount + 1)
            .ToListAsync(cancellationToken);
        var expectedEdits = 0;
        for (var hunkIndex = 0; hunkIndex < hunks.Count; hunkIndex++)
        {
            var stored = hunks[hunkIndex];
            var expected = result.Hunks[hunkIndex];
            if (stored.Position != hunkIndex || stored.BeforeStart != expected.BeforeStart ||
                stored.BeforeLength != expected.BeforeLength || stored.AfterStart != expected.AfterStart ||
                stored.AfterLength != expected.AfterLength || stored.BeforeContextStart != expected.BeforeContextStart ||
                stored.BeforeContextLength != expected.BeforeContext.Length || stored.AfterContextStart != expected.AfterContextStart ||
                stored.AfterContextLength != expected.AfterContext.Length || stored.WordEditCount != expected.WordEdits.Length)
                throw new InvalidOperationException("Stored comparison hunk details are invalid.");
            for (var editIndex = 0; editIndex < expected.WordEdits.Length; editIndex++)
            {
                if (expectedEdits >= edits.Count) throw new InvalidOperationException("Stored comparison word edits are incomplete.");
                var edit = edits[expectedEdits++];
                var expectedEdit = expected.WordEdits[editIndex];
                if (edit.HunkPosition != hunkIndex || edit.Position != editIndex || edit.BeforeStart != expectedEdit.BeforeStart ||
                    edit.BeforeLength != expectedEdit.BeforeLength || edit.AfterStart != expectedEdit.AfterStart ||
                    edit.AfterLength != expectedEdit.AfterLength)
                    throw new InvalidOperationException("Stored comparison word edits are invalid.");
            }
        }
        if (expectedEdits != edits.Count)
            throw new InvalidOperationException("Stored comparison has extra word edits.");
    }

    private static async Task InsertHunksAsync(CollectionAttemptDbContext db, DocumentComparison comparison,
        CancellationToken cancellationToken)
    {
        var hunks = comparison.Hunks.Select((hunk, position) => new DocumentComparisonHunkRow
        {
            ComparisonId = comparison.ComparisonId,
            Position = position,
            BeforeStart = hunk.BeforeStart,
            BeforeLength = hunk.BeforeLength,
            AfterStart = hunk.AfterStart,
            AfterLength = hunk.AfterLength,
            BeforeContextStart = hunk.BeforeContextStart,
            BeforeContextLength = hunk.BeforeContext.Length,
            AfterContextStart = hunk.AfterContextStart,
            AfterContextLength = hunk.AfterContext.Length,
            WordEditCount = hunk.WordEdits.Length
        }).ToArray();
        var edits = comparison.Hunks.SelectMany((hunk, hunkPosition) => hunk.WordEdits.Select((edit, position) =>
            new DocumentComparisonWordEditRow
            {
                ComparisonId = comparison.ComparisonId,
                HunkPosition = hunkPosition,
                Position = position,
                BeforeStart = edit.BeforeStart,
                BeforeLength = edit.BeforeLength,
                AfterStart = edit.AfterStart,
                AfterLength = edit.AfterLength
            })).ToArray();
        await db.Set<DocumentComparisonHunkRow>().AddRangeAsync(hunks, cancellationToken);
        await db.Set<DocumentComparisonWordEditRow>().AddRangeAsync(edits, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }

    private static string Serialize(DocumentComparison comparison) =>
        JsonSerializer.Serialize(comparison, CollectionProtocol.JsonOptions);

    private static void ValidateId(string id)
    {
        if (!LowercaseHex.Is(id, 64))
            throw new ArgumentException("Comparison ID must be a lowercase SHA-256 value.", nameof(id));
    }
}
