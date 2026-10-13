using CivicLens.Application.Collection;
using Microsoft.EntityFrameworkCore;

namespace CivicLens.Infrastructure.Collection.Jobs;

internal sealed class ConfigurationRevisionWriter(CollectionAttemptDbContext db, long createdAt)
{
    private readonly HashSet<string> storedRevisionIds = new(StringComparer.Ordinal);

    public void MarkStored(IEnumerable<string> revisionIds) => storedRevisionIds.UnionWith(revisionIds);

    public async Task<string?> StoreAsync(CollectionConfigurationRevision? revision, CancellationToken cancellationToken)
    {
        if (revision is null || !storedRevisionIds.Add(revision.Id)) return revision?.Id;
        _ = await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO collection_configuration_revisions (revision_id, json, created_at)
            VALUES ({revision.Id}, {revision.Json}, {createdAt})
            ON CONFLICT (revision_id) DO NOTHING
            """, cancellationToken);
        return revision.Id;
    }
}
