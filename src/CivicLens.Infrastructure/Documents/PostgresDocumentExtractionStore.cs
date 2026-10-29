using System.Data;
using System.Text.Json;
using CivicLens.Application.Documents;
using CivicLens.Collection.Contracts;
using CivicLens.Core.Collection;
using CivicLens.Core.Documents;
using CivicLens.Core;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Documents.Persistence;
using CivicLens.Infrastructure.Statements;
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
        var captured = await RequireImportedAsync(db, extraction, cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        if (await InsertAsync(db, captured, extraction, cancellationToken) is { } winner) return winner;
        await PostgresStatementCandidateRegistration.RegisterAsync(db, captured, extraction.ExtractionId, extraction.Text, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return extraction;
    }

    public async Task<int> SaveFeedItemsAsync(IReadOnlyList<FeedItemExtraction> newestFirst, int maximumNew,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(newestFirst);
        if (newestFirst.Count == 0) return 0;
        var feed = newestFirst[0].Extraction;
        if (newestFirst.Any(item => !item.Extraction.SourceAttempt.Equals(feed.SourceAttempt)))
            throw new ArgumentException("Feed items must share one captured attempt.", nameof(newestFirst));
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var captured = await RequireImportedAsync(db, feed, cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var known = await PostgresStatementCandidateRegistration.ReadKnownAsync(db, captured, newestFirst.Select(item => item.Link),
            cancellationToken);
        var remaining = maximumNew - known.RegisteredForAttempt;
        var registered = 0;
        foreach (var item in newestFirst)
        {
            if (registered >= remaining) break;
            if (known.Links.Contains(item.Link)) continue;
            if (await InsertAsync(db, captured, item.Extraction, cancellationToken) is not null) continue;
            if (await PostgresStatementCandidateRegistration.RegisterFeedItemAsync(db, captured, item.Link,
                    item.Extraction.ExtractionId, item.Extraction.Text, cancellationToken))
                registered++;
        }
        await transaction.CommitAsync(cancellationToken);
        return registered;
    }

    private static async Task<CapturedAttemptResult> RequireImportedAsync(CollectionAttemptDbContext db,
        DocumentExtraction extraction, CancellationToken cancellationToken)
    {
        var imported = await PostgresCollectionAttemptStore.GetAsync(db, extraction.SourceAttempt.AttemptId, cancellationToken,
            includeDiscovery: false);
        if (imported?.AttemptResult is not CapturedAttemptResult captured || !captured.Equals(extraction.SourceAttempt))
            throw new InvalidOperationException("Extraction provenance does not match an imported captured attempt.");
        return captured;
    }

    /// <summary>Returns null when the row is new, or the retained row of an equal replay.</summary>
    private static async Task<DocumentExtraction?> InsertAsync(CollectionAttemptDbContext db, CapturedAttemptResult captured,
        DocumentExtraction extraction, CancellationToken cancellationToken)
    {
        var profileJson = SerializeProfile(extraction.Profile);
        var inserted = await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO document_extractions (extraction_id, attempt_id, extraction_version, text,
                text_sha256, profile_json, part)
            VALUES ({extraction.ExtractionId}, {captured.AttemptId}, {extraction.ExtractionVersion},
                {extraction.Text}, {extraction.TextSha256}, {profileJson}, {extraction.Part})
            ON CONFLICT (extraction_id) DO NOTHING
            """, cancellationToken);
        if (inserted > 0) return null;
        var winner = await ReadAsync(db, extraction.ExtractionId, cancellationToken)
            ?? throw new InvalidOperationException("A conflicting extraction row vanished.");
        return SameEvidence(winner, extraction)
            ? winner
            : throw new InvalidOperationException("An extraction identity was reused with different evidence or text.");
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
        return FromRow(row, captured);
    }

    internal static async Task<Dictionary<string, DocumentExtraction>> ReadManyAsync(CollectionAttemptDbContext db,
        string[] extractionIds, CancellationToken cancellationToken)
    {
        var rows = await db.Set<DocumentExtractionRow>().AsNoTracking()
            .Where(row => extractionIds.Contains(row.ExtractionId)).ToArrayAsync(cancellationToken);
        var attemptIds = rows.Select(row => row.AttemptId).Distinct(StringComparer.Ordinal).ToArray();
        var attemptRows = await db.Attempts.AsNoTracking()
            .Where(row => attemptIds.Contains(row.AttemptId)).ToListAsync(cancellationToken);
        var captured = (await PostgresCollectionAttemptStore.ReadEvidenceAsync(db, attemptRows, cancellationToken))
            .Select(attempt => attempt.AttemptResult).OfType<CapturedAttemptResult>()
            .ToDictionary(attempt => attempt.AttemptId, StringComparer.Ordinal);
        return rows.ToDictionary(row => row.ExtractionId, row => FromRow(row,
            captured.GetValueOrDefault(row.AttemptId)
                ?? throw new InvalidOperationException("Stored extraction has no captured source attempt.")),
            StringComparer.Ordinal);
    }

    internal static DocumentExtraction FromRow(DocumentExtractionRow row, CapturedAttemptResult captured)
    {
        if (row.AttemptId != captured.AttemptId)
            throw new InvalidOperationException("Stored extraction provenance does not match its captured attempt.");
        DocumentExtraction extraction;
        try
        {
            var profile = row.ProfileJson is null ? null :
                (JsonSerializer.Deserialize<DocumentProfileConfiguration>(row.ProfileJson, CollectionProtocol.JsonOptions)
                    ?? throw new JsonException("Stored profile is null.")).ToProfile();
            extraction = new DocumentExtraction(captured, row.ExtractionVersion, row.Text, profile, row.Part);
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
        left.SourceAttempt.Equals(right.SourceAttempt) && left.ExtractionVersion == right.ExtractionVersion &&
        left.Text == right.Text && left.Part == right.Part &&
        (left.Profile is null ? right.Profile is null : left.Profile.Matches(right.Profile));

    private static string? SerializeProfile(DocumentContentProfile? profile) => profile is null ? null :
        JsonSerializer.Serialize(new DocumentProfileConfiguration
        {
            Id = profile.Id,
            Selector = profile.Selector,
            ExcludedSelectors = profile.ExcludedSelectors.ToArray()
        }, CollectionProtocol.JsonOptions);

    internal static void ValidateId(string extractionId)
    {
        if (!LowercaseHex.Is(extractionId, 64))
            throw new ArgumentException("Extraction ID must be a lowercase SHA-256 value.", nameof(extractionId));
    }
}
