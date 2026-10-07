using System.Data;
using System.Text.Json;
using CivicLens.Application.Documents;
using CivicLens.Collection.Contracts;
using CivicLens.Core.Documents;
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

    public async Task<DocumentComparison> SaveAsync(DocumentComparison comparison, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(comparison);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        await db.Database.ExecuteSqlRawAsync(
            "SELECT pg_advisory_xact_lock(hashtextextended('civic-lens-document-comparisons', 0))", cancellationToken);
        var verified = await ReconstructAsync(db, comparison.BeforeExtractionId, comparison.AfterExtractionId, cancellationToken);
        if (Serialize(verified) != Serialize(comparison))
            throw new InvalidOperationException("Comparison does not match retained extraction evidence.");
        var existing = await db.Set<DocumentComparisonRow>().AsNoTracking()
            .SingleOrDefaultAsync(row => row.ComparisonId == comparison.ComparisonId, cancellationToken);
        if (existing is not null)
        {
            ValidateRetained(existing, verified);
            await transaction.CommitAsync(cancellationToken);
            return verified;
        }

        db.Add(new DocumentComparisonRow
        {
            ComparisonId = comparison.ComparisonId,
            BeforeExtractionId = comparison.BeforeExtractionId,
            AfterExtractionId = comparison.AfterExtractionId,
            AlgorithmVersion = comparison.AlgorithmVersion,
            SettingsVersion = comparison.SettingsVersion,
            ResultJson = Serialize(comparison)
        });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return verified;
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
        if (id is not { Length: 64 } || id.Any(c => c is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
            throw new ArgumentException("Comparison ID must be a lowercase SHA-256 value.", nameof(id));
    }
}
