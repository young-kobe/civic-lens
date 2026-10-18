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

    public static async Task<ReleaseLinks> LoadAsync(IReleaseDirectory releases, PreviousRelease previous,
        IReadOnlyList<PendingRecord> additions, CancellationToken cancellationToken)
    {
        if (!additions.Any(addition => ListsRelated(addition.Entry.Kind))) return None;
        ImmutableArray<PublishedRelationship> added = [.. additions.Where(addition => addition.Entry.Kind == PublishedRecordKind.Relationship)
            .Select(addition => Parse(addition.Json))];
        var replaced = added.Select(link => link.RecordId).ToHashSet(StringComparer.Ordinal);
        var carried = new List<PublishedRelationship>();
        foreach (var entry in previous.Entries.Where(entry => entry.Kind == PublishedRecordKind.Relationship && !replaced.Contains(entry.RecordId)))
            carried.Add(Parse(await releases.ReadFileAsync(previous.DirectoryName!, PublicationProtocol.RecordDataPath(entry.RecordId),
                PublicationProtocol.MaximumRecordBytes, cancellationToken)));
        return new([.. carried, .. added], added);
    }

    public ImmutableArray<PublishedRelationship> Citing(PublishedRecordKind kind, string recordId) =>
        [.. all.Where(link => Cites(link.Statement, kind, recordId) || Cites(link.Target, kind, recordId))
            .OrderBy(link => link.StatementDate is null).ThenBy(link => link.StatementDate).ThenBy(link => link.RecordId, StringComparer.Ordinal)];

    private static bool ListsRelated(PublishedRecordKind kind) =>
        kind is PublishedRecordKind.Statement or PublishedRecordKind.Action or PublishedRecordKind.Vote or PublishedRecordKind.Relationship;

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
