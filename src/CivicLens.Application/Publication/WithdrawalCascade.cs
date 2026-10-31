using System.Collections.Immutable;
using CivicLens.Application.Briefs;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

/// <summary>Refuses a withdrawal while a relationship or a brief that stays in the new release cites the withdrawn record.</summary>
internal static class WithdrawalCascade
{
    public static async Task RequireNoCitersAsync(IReleaseDirectory releases, PublishedRecordLoaders loaders, PreviousRelease previous,
        ImmutableArray<PublishedRecordReference> removed, IReadOnlyList<PendingRecord> additions, CancellationToken cancellationToken)
    {
        var addedIds = additions.Select(addition => addition.RecordId).ToHashSet(StringComparer.Ordinal);
        var citers = await FindCitersAsync(releases, loaders, previous, removed, addedIds, cancellationToken);
        if (citers.Count > 0)
            throw new ArgumentException(
                $"A withdrawn record is still cited. Withdraw the citing records in the same request: {string.Join(", ", citers.Select(citer => citer.ToString()).Order(StringComparer.Ordinal))}.");
    }

    public static async Task<List<PublishedRecordReference>> FindCitersAsync(IReleaseDirectory releases, PublishedRecordLoaders loaders,
        PreviousRelease previous, ImmutableArray<PublishedRecordReference> removed, IReadOnlySet<string> addedIds,
        CancellationToken cancellationToken)
    {
        var staying = previous.Entries.Where(entry => !removed.Contains(new(entry.Kind, entry.RecordId)) && !addedIds.Contains(entry.RecordId)).ToArray();
        var citers = new List<PublishedRecordReference>();
        citers.AddRange(await CitingRelationshipsAsync(loaders.Relationships, staying, removed, cancellationToken));
        citers.AddRange(await CitingBriefsAsync(releases, previous, staying, removed, cancellationToken));
        return citers;
    }

    private static async Task<IEnumerable<PublishedRecordReference>> CitingRelationshipsAsync(RelationshipRecordLoader relationships,
        PublishedRecordEntry[] staying, ImmutableArray<PublishedRecordReference> removed, CancellationToken cancellationToken)
    {
        var cited = removed.Where(record => PublishedRelationship.IsCitedKind(record.Kind)).ToArray();
        if (cited.Length == 0) return [];
        var carriedIds = staying.Where(entry => entry.Kind == PublishedRecordKind.Relationship).Select(entry => entry.RecordId).ToImmutableArray();
        var citing = await relationships.FindCitingAsync(carriedIds, cited, cancellationToken);
        return citing.Select(recordId => new PublishedRecordReference(PublishedRecordKind.Relationship, recordId));
    }

    private static async Task<IEnumerable<PublishedRecordReference>> CitingBriefsAsync(IReleaseDirectory releases, PreviousRelease previous,
        PublishedRecordEntry[] staying, ImmutableArray<PublishedRecordReference> removed, CancellationToken cancellationToken)
    {
        var withdrawn = removed.Where(record => BriefCitationKinds.IsCitable(record.Kind)).ToHashSet();
        if (withdrawn.Count == 0) return [];
        var citing = new List<PublishedRecordReference>();
        foreach (var entry in staying.Where(entry => entry.Kind == PublishedRecordKind.Brief))
        {
            var brief = await ReleaseRecordFiles.ReadAsync<PublishedBrief>(releases, entry, [], previous, cancellationToken);
            if (brief.Citations.Any(citation => withdrawn.Contains(new(citation.Kind, citation.RecordId))))
                citing.Add(new(PublishedRecordKind.Brief, entry.RecordId));
        }
        return citing;
    }
}
