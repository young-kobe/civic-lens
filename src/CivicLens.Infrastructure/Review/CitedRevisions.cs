using System.Collections.Immutable;
using CivicLens.Application.Publication;
using CivicLens.Infrastructure.Collection;
using CivicLens.Publication.Contracts;
using Microsoft.EntityFrameworkCore;

namespace CivicLens.Infrastructure.Review;

internal static class CitedRevisions
{
    public static async Task<HashSet<PublishedRevisionBinding>> FindNotCurrentInActiveReleaseAsync(CollectionAttemptDbContext db,
        IEnumerable<PublishedRevisionBinding> cited, CancellationToken cancellationToken) =>
        await FindNotCurrentAsync(db, cited, await ReviewQueries.ReadActiveReleaseRecordsAsync(db, cancellationToken), cancellationToken);

    // A cited revision is current when the release holds it and the latest decision on it is an approval.
    public static async Task<HashSet<PublishedRevisionBinding>> FindNotCurrentAsync(CollectionAttemptDbContext db,
        IEnumerable<PublishedRevisionBinding> cited, ImmutableArray<PublishedRevisionBinding> releaseRecords, CancellationToken cancellationToken)
    {
        var bindings = cited.Distinct().ToArray();
        if (bindings.Length == 0) return [];
        var released = releaseRecords.ToDictionary(binding => (binding.Kind, binding.RecordId), binding => binding.RevisionNumber);
        var notCurrent = bindings.Where(binding => released.GetValueOrDefault((binding.Kind, binding.RecordId)) != binding.RevisionNumber).ToHashSet();
        foreach (var group in bindings.GroupBy(binding => binding.Kind))
            notCurrent.UnionWith(await FindNotApprovedAsync(db, group.Key, [.. group], cancellationToken));
        return notCurrent;
    }

    public static async Task<string[]> ReadRevisionJsonAsync(CollectionAttemptDbContext db, PublishedRecordKind kind,
        IEnumerable<PublishedRevisionBinding> named, CancellationToken cancellationToken)
    {
        var requested = named.Distinct().ToArray();
        var stored = await ReadStoredRevisionsAsync(db, kind, requested, cancellationToken);
        if (stored.Length != requested.Length) throw new InvalidDataException($"A named {kind} revision is not stored.");
        return [.. stored.Select(row => row.RevisionJson)];
    }

    /// <summary>Reads the named revisions that are stored. A revision that is not stored is absent from the result.</summary>
    public static async Task<StoredRevisionRow[]> ReadStoredRevisionsAsync(CollectionAttemptDbContext db, PublishedRecordKind kind,
        PublishedRevisionBinding[] requested, CancellationToken cancellationToken)
    {
        if (requested.Length == 0) return [];
        RequireKind(kind, requested, nameof(requested));
        var (table, _, idColumn) = ReviewTables.For(kind);
        var sql = $"SELECT r.{idColumn} AS \"RecordId\", r.revision_number AS \"RevisionNumber\", r.revision_json AS \"RevisionJson\" FROM {table} r JOIN unnest({{0}}, {{1}}) AS k(id, number) ON r.{idColumn} = k.id AND r.revision_number = k.number";
        return await db.Database.SqlQueryRaw<StoredRevisionRow>(sql, requested.Select(binding => binding.RecordId).ToArray(),
            requested.Select(binding => binding.RevisionNumber).ToArray()).ToArrayAsync(cancellationToken);
    }

    public static async Task<PublishedRevisionBinding[]> FindNotApprovedAsync(CollectionAttemptDbContext db, PublishedRecordKind kind,
        PublishedRevisionBinding[] requested, CancellationToken cancellationToken)
    {
        if (requested.Length == 0) return [];
        RequireKind(kind, requested, nameof(requested));
        var (_, decisionTable, idColumn) = ReviewTables.For(kind);
        var sql = $$"""
            SELECT k.id AS "RecordId", k.number AS "RevisionNumber" FROM unnest({0}, {1}) AS k(id, number)
            WHERE (SELECT d.kind FROM {{decisionTable}} d WHERE d.{{idColumn}} = k.id AND d.revision_number = k.number
                   ORDER BY d.review_state_version DESC LIMIT 1) IS DISTINCT FROM 'Approve'
            """;
        var rows = await db.Database.SqlQueryRaw<NotApprovedRow>(sql, requested.Select(binding => binding.RecordId).ToArray(),
            requested.Select(binding => binding.RevisionNumber).ToArray()).ToArrayAsync(cancellationToken);
        return [.. rows.Select(row => new PublishedRevisionBinding(kind, row.RecordId, row.RevisionNumber))];
    }

    public static void RequireKind(PublishedRecordKind kind, PublishedRevisionBinding[] bindings, string parameterName)
    {
        if (bindings.Any(binding => binding.Kind != kind))
            throw new ArgumentException($"Every named revision must be a {kind} revision.", parameterName);
    }

    public sealed class StoredRevisionRow
    {
        public string RecordId { get; set; } = "";
        public int RevisionNumber { get; set; }
        public string RevisionJson { get; set; } = "";
    }

    private sealed class NotApprovedRow
    {
        public string RecordId { get; set; } = "";
        public int RevisionNumber { get; set; }
    }
}
