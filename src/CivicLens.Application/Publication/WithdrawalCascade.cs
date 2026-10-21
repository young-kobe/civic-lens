using System.Collections.Immutable;
using CivicLens.Application.Briefs;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

/// <summary>Refuses a withdrawal while a relationship or a brief that stays in the new release cites the withdrawn record.</summary>
internal static class WithdrawalCascade
{
    public static async Task RequireNoCitersAsync(IReleaseDirectory releases, PublishedRecordLoaders loaders, PublicationBuild build,
        IReadOnlyList<PendingRecord> additions, CancellationToken cancellationToken)
    {
        var addedIds = additions.Select(addition => addition.RecordId).ToHashSet(StringComparer.Ordinal);
        var citers = new List<PublishedRecordReference>();
        citers.AddRange(await CitingRelationshipsAsync(loaders.Relationships, build, addedIds, cancellationToken));
        citers.AddRange(await CitingBriefsAsync(releases, build, addedIds, cancellationToken));
        if (citers.Count > 0)
            throw new ArgumentException(
                $"A withdrawn record is still cited. Withdraw the citing records in the same request: {string.Join(", ", citers.Select(citer => citer.ToString()).Order(StringComparer.Ordinal))}.");
    }

    private static async Task<IEnumerable<PublishedRecordReference>> CitingRelationshipsAsync(RelationshipRecordLoader relationships,
        PublicationBuild build, HashSet<string> addedIds, CancellationToken cancellationToken)
    {
        var cited = build.Removed.Where(record => PublishedRelationship.IsCitedKind(record.Kind)).ToArray();
        if (cited.Length == 0) return [];
        var carriedIds = build.ReleaseRecords
            .Where(record => record.Kind == PublishedRecordKind.Relationship && !addedIds.Contains(record.RecordId))
            .Select(record => record.RecordId).ToImmutableArray();
        var citing = await relationships.FindCitingAsync(carriedIds, cited, cancellationToken);
        return citing.Select(recordId => new PublishedRecordReference(PublishedRecordKind.Relationship, recordId));
    }

    private static async Task<IEnumerable<PublishedRecordReference>> CitingBriefsAsync(IReleaseDirectory releases, PublicationBuild build,
        HashSet<string> addedIds, CancellationToken cancellationToken)
    {
        var withdrawn = build.Removed.Where(record => BriefCitationKinds.IsCitable(record.Kind)).ToHashSet();
        if (withdrawn.Count == 0) return [];
        var citing = new List<PublishedRecordReference>();
        var kept = build.ReleaseRecords.Select(record => record.RecordId).ToHashSet(StringComparer.Ordinal);
        foreach (var entry in build.Previous.Entries.Where(entry => entry.Kind == PublishedRecordKind.Brief &&
                     kept.Contains(entry.RecordId) && !addedIds.Contains(entry.RecordId)))
        {
            var brief = await ReleaseRecordFiles.ReadAsync<PublishedBrief>(releases, entry, [], build.Previous, cancellationToken);
            if (brief.Citations.Any(citation => withdrawn.Contains(new(citation.Kind, citation.RecordId))))
                citing.Add(new(PublishedRecordKind.Brief, entry.RecordId));
        }
        return citing;
    }
}
