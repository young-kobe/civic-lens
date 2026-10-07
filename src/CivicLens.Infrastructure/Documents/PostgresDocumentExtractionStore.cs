using System.Data;
using System.Text.Json;
using CivicLens.Application.Documents;
using CivicLens.Collection.Contracts;
using CivicLens.Core.Collection;
using CivicLens.Core.Documents;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Documents.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace CivicLens.Infrastructure.Documents;

/// <summary>Retains immutable text extractions against already imported capture evidence.</summary>
public sealed class PostgresDocumentExtractionStore(IDbContextFactory<CollectionAttemptDbContext> contextFactory)
    : IDocumentExtractionStore
{
    public static PostgresDocumentExtractionStore FromConnectionString(string connectionString)
    {
        // Reuse the existing boundary validation without exposing provider diagnostics.
        _ = PostgresCollectionAttemptStore.FromConnectionString(connectionString);
        var options = new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString).Options;
        return new PostgresDocumentExtractionStore(new PooledDbContextFactory<CollectionAttemptDbContext>(options));
    }

    public async Task<DocumentExtraction?> GetAsync(string extractionId, CancellationToken cancellationToken)
    {
        ValidateId(extractionId);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await ReadAsync(db, extractionId, cancellationToken);
    }

    public async Task<DocumentExtraction> SaveAsync(DocumentExtraction extraction, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(extraction);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        // As with collection imports, serialize the read/compare/write decision in one short transaction.
        await db.Database.ExecuteSqlRawAsync(
            "SELECT pg_advisory_xact_lock(hashtextextended('civic-lens-document-extractions', 0))", cancellationToken);
        var imported = await PostgresCollectionAttemptStore.GetAsync(db, extraction.SourceAttempt.AttemptId, cancellationToken,
            includeDiscovery: false);
        if (imported?.AttemptResult is not CapturedAttemptResult captured || !captured.Equals(extraction.SourceAttempt))
            throw new InvalidOperationException("Extraction provenance does not match an imported captured attempt.");

        var existing = await ReadAsync(db, extraction.ExtractionId, cancellationToken);
        if (existing is not null)
        {
            if (!SameEvidence(existing, extraction))
                throw new InvalidOperationException("An extraction identity was reused with different evidence or text.");
            await transaction.CommitAsync(cancellationToken);
            return existing;
        }

        db.Add(new DocumentExtractionRow
        {
            ExtractionId = extraction.ExtractionId,
            AttemptId = extraction.SourceAttempt.AttemptId,
            ParserVersion = extraction.ParserVersion,
            NormalizationVersion = extraction.NormalizationVersion,
            Text = extraction.Text,
            TextSha256 = extraction.TextSha256,
            ProfileJson = SerializeProfile(extraction.Profile)
        });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return extraction;
    }

    internal static async Task<DocumentExtraction?> ReadAsync(CollectionAttemptDbContext db, string extractionId,
        CancellationToken cancellationToken)
    {
        var row = await db.Set<DocumentExtractionRow>().AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.ExtractionId == extractionId, cancellationToken);
        if (row is null) return null;
        var imported = await PostgresCollectionAttemptStore.GetAsync(db, row.AttemptId, cancellationToken,
            includeDiscovery: false);
        if (imported?.AttemptResult is not CapturedAttemptResult captured)
            throw new InvalidOperationException("Stored extraction has no captured source attempt.");
        DocumentExtraction extraction;
        try
        {
            var profile = row.ProfileJson is null ? null :
                (JsonSerializer.Deserialize<DocumentProfileConfiguration>(row.ProfileJson, CollectionProtocol.JsonOptions)
                    ?? throw new JsonException("Stored profile is null.")).ToProfile();
            extraction = new DocumentExtraction(captured, row.ParserVersion, row.NormalizationVersion, row.Text, profile);
        }
        catch (Exception exception) when (exception is ArgumentException or JsonException)
        {
            throw new InvalidOperationException("Stored extraction evidence is invalid.", exception);
        }
        if (extraction.ExtractionId != row.ExtractionId || extraction.TextSha256 != row.TextSha256)
            throw new InvalidOperationException("Stored extraction identity or text hash is invalid.");
        return extraction;
    }

    private static bool SameEvidence(DocumentExtraction left, DocumentExtraction right) =>
        left.SourceAttempt.Equals(right.SourceAttempt) && left.ParserVersion == right.ParserVersion &&
        left.NormalizationVersion == right.NormalizationVersion && left.Text == right.Text &&
        (left.Profile is null ? right.Profile is null : left.Profile.Matches(right.Profile));

    private static string? SerializeProfile(DocumentContentProfile? profile) => profile is null ? null :
        JsonSerializer.Serialize(new DocumentProfileConfiguration
        {
            Id = profile.Id,
            Selector = profile.Selector,
            ExcludedSelectors = profile.ExcludedSelectors.ToArray()
        }, CollectionProtocol.JsonOptions);

    private static void ValidateId(string extractionId)
    {
        if (extractionId is not { Length: 64 } || extractionId.Any(c => c is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
            throw new ArgumentException("Extraction ID must be a lowercase SHA-256 value.", nameof(extractionId));
    }
}
