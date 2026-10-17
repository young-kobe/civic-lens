using System.Collections.Immutable;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Collection.Jobs;

namespace CivicLens.Infrastructure.Review;

internal static class ConfiguredPersonNames
{
    public static async Task<ImmutableDictionary<string, string>> ReadAsync(CollectionAttemptDbContext db,
        IReadOnlyCollection<string> candidateIds, IReadOnlyDictionary<string, string> revisionIdByCandidate,
        CancellationToken cancellationToken)
    {
        if (candidateIds.Any(id => !revisionIdByCandidate.ContainsKey(id)))
            throw new InvalidDataException("A revision names a candidate that is missing.");
        var ids = revisionIdByCandidate.Values.Distinct(StringComparer.Ordinal).ToArray();
        var configurations = await StoredJobDefinition.LoadRevisionsAsync(db, ids, cancellationToken);
        return ids.SelectMany(id => StoredJobDefinition.Find(configurations, id).Configuration.People)
            .DistinctBy(person => person.Id, StringComparer.Ordinal)
            .ToImmutableDictionary(person => person.Id, person => person.Name, StringComparer.Ordinal);
    }
}
