using CivicLens.Application.Collection;
using CivicLens.Application.Collection.Jobs;
using CivicLens.Infrastructure.Collection.Jobs.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CivicLens.Infrastructure.Collection.Jobs;

internal sealed class JobRowWriter(CollectionAttemptDbContext db, long createdAt)
{
    private readonly HashSet<string> storedRevisionIds = new(StringComparer.Ordinal);

    public void MarkStored(IEnumerable<string> revisionIds) => storedRevisionIds.UnionWith(revisionIds);

    public async Task<JobRow> AddJobAsync(string idempotencyKey, CollectionJobDefinition definition,
        CancellationToken cancellationToken)
    {
        var row = new JobRow
        {
            JobId = Guid.NewGuid().ToString("N"),
            IdempotencyKey = idempotencyKey,
            DefinitionJson = StoredJobDefinition.Write(definition),
            SourceId = definition.SourceId,
            IsRoot = definition.IsRoot(),
            ConfigurationRevisionId = await StoreAsync(definition.ConfigurationRevision, cancellationToken),
            PacingKey = PostgresCollectionJobStore.PacingKeyOf(definition),
            Priority = definition.Priority,
            State = CollectionJobState.Pending,
            CreatedAt = createdAt
        };
        db.Add(row);
        return row;
    }

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
