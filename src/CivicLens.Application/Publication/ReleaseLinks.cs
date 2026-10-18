using System.Collections.Immutable;
using System.Text.Json;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

/// <summary>The relationships of the new release: the carried ones plus the ones this publish adds.</summary>
internal sealed class ReleaseLinks
{
    private static readonly ReleaseLinks None = new([], []);

    private readonly ImmutableArray<PublishedRelationship> all;

    private ReleaseLinks(ImmutableArray<PublishedRelationship> all, ImmutableArray<PublishedRelationship> added)
    {
        this.all = all;
        Added = added;
    }

    public ImmutableArray<PublishedRelationship> Added { get; }

    // The database names the carried relationships that cite a record this publish renders. The files stay the source of the text.
    public static async Task<ReleaseLinks> LoadAsync(IReleaseDirectory releases, PublicationBuild build, RelationshipRecordLoader relationships,
        IReadOnlyList<PendingRecord> additions, CancellationToken cancellationToken)
    {
        if (!additions.Any(addition => ListsRelated(addition.Entry.Kind))) return None;
        ImmutableArray<PublishedRelationship> added = [.. additions.Where(addition => addition.Entry.Kind == PublishedRecordKind.Relationship)
            .Select(addition => Parse(addition.Json))];
        var replaced = added.Select(link => link.RecordId).ToHashSet(StringComparer.Ordinal);
        var carriedIds = build.ReleaseRecords.Where(record => record.Kind == PublishedRecordKind.Relationship && !replaced.Contains(record.RecordId))
            .Select(record => record.RecordId).ToImmutableArray();
        var citing = await relationships.FindCitingAsync(carriedIds, RenderedRecords(additions, added), cancellationToken);
        var carried = new List<PublishedRelationship>();
        foreach (var recordId in citing.Order(StringComparer.Ordinal))
            carried.Add(Parse(await releases.ReadFileAsync(build.Previous.DirectoryName!, PublicationProtocol.RecordDataPath(recordId),
                PublishedRelationship.MaximumFileBytes, cancellationToken)));
        return new([.. carried, .. added], added);
    }

    public ImmutableArray<PublishedRelationship> Citing(PublishedRecordKind kind, string recordId) =>
        [.. all.Where(link => Cites(link.Statement, kind, recordId) || Cites(link.Target, kind, recordId))
            .OrderBy(link => link.StatementDate is null).ThenBy(link => link.StatementDate).ThenBy(link => link.RecordId, StringComparer.Ordinal)];

    private static bool ListsRelated(PublishedRecordKind kind) =>
        PublishedRelationship.IsCitedKind(kind) || kind == PublishedRecordKind.Relationship;

    private static List<PublishedRecordReference> RenderedRecords(IReadOnlyList<PendingRecord> additions, ImmutableArray<PublishedRelationship> added) =>
        [.. additions.Where(addition => PublishedRelationship.IsCitedKind(addition.Entry.Kind))
            .Select(addition => new PublishedRecordReference(addition.Entry.Kind, addition.RecordId))
            .Concat(added.SelectMany(link => new[] { link.Statement, link.Target }).Select(cited => new PublishedRecordReference(cited.Kind, cited.RecordId)))
            .Distinct()];

    private static bool Cites(PublishedRelationshipCitation citation, PublishedRecordKind kind, string recordId) =>
        citation.Kind == kind && citation.RecordId == recordId;

    private static PublishedRelationship Parse(byte[] json)
    {
        var link = JsonSerializer.Deserialize<PublishedRelationship>(json, PublicationProtocol.JsonOptions)
            ?? throw new InvalidDataException("A relationship file is empty.");
        link.Validate();
        return link;
    }
}
