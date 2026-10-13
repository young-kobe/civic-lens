using System.Text.Json;
using CivicLens.Application.Collection;
using CivicLens.Application.Collection.Jobs;
using CivicLens.Collection.Contracts;
using CivicLens.Infrastructure.Collection.Jobs.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CivicLens.Infrastructure.Collection.Jobs;

internal static class StoredJobDefinition
{
    public static CollectionJobDefinition WithoutRevision(CollectionJobDefinition definition) =>
        definition with { ConfigurationRevision = null };

    public static string Write(CollectionJobDefinition definition) =>
        JsonSerializer.Serialize(WithoutRevision(definition), CollectionProtocol.JsonOptions);

    public static async Task<IReadOnlyDictionary<string, CollectionConfigurationRevision>> LoadRevisionsAsync(
        CollectionAttemptDbContext db, IEnumerable<string?> revisionIds, CancellationToken cancellationToken)
    {
        var ids = revisionIds.OfType<string>().Distinct(StringComparer.Ordinal).ToArray();
        if (ids.Length == 0) return new Dictionary<string, CollectionConfigurationRevision>();
        var rows = await db.Set<ConfigurationRevisionRow>().AsNoTracking()
            .Where(row => ids.Contains(row.RevisionId)).ToListAsync(cancellationToken);
        return rows.ToDictionary(row => row.RevisionId, ReadRevision, StringComparer.Ordinal);
    }

    public static CollectionConfigurationRevision Find(
        IReadOnlyDictionary<string, CollectionConfigurationRevision> revisions, string revisionId) =>
        revisions.GetValueOrDefault(revisionId)
        ?? throw new InvalidDataException("A stored row references a missing configuration revision.");

    public static CollectionJobDefinition Read(string storedJson, string? revisionId,
        IReadOnlyDictionary<string, CollectionConfigurationRevision> revisions)
    {
        try
        {
            var definition = JsonSerializer.Deserialize<CollectionJobDefinition>(storedJson, CollectionProtocol.JsonOptions)
                ?? throw new InvalidDataException("Missing job data.");
            if (definition.ConfigurationRevision is not null)
                throw new InvalidDataException("Stored job definition embeds a configuration revision.");
            if (revisionId is not null)
                definition = definition with { ConfigurationRevision = Find(revisions, revisionId) };
            definition.Validate();
            return definition;
        }
        catch (Exception exception) when (exception is ArgumentException or JsonException)
        {
            throw new InvalidDataException("Stored job definition is invalid.", exception);
        }
    }

    public static async Task<CollectionJobDefinition> ReadAsync(CollectionAttemptDbContext db, string storedJson,
        string? revisionId, CancellationToken cancellationToken) =>
        Read(storedJson, revisionId, await LoadRevisionsAsync(db, [revisionId], cancellationToken));

    private static CollectionConfigurationRevision ReadRevision(ConfigurationRevisionRow row)
    {
        try
        {
            return new CollectionConfigurationRevision(row.RevisionId, row.Json);
        }
        catch (Exception exception) when (exception is ArgumentException or JsonException)
        {
            throw new InvalidDataException("Stored configuration revision is invalid.", exception);
        }
    }
}
