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

    public static async Task<PublishedRevisionBinding[]> FindNotApprovedAsync(CollectionAttemptDbContext db, PublishedRecordKind kind,
        PublishedRevisionBinding[] requested, CancellationToken cancellationToken)
    {
        if (requested.Length == 0) return [];
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

    private sealed class NotApprovedRow
    {
        public string RecordId { get; set; } = "";
        public int RevisionNumber { get; set; }
    }
}
