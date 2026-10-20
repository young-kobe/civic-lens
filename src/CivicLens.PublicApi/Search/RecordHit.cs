using System.Text.Json.Serialization;
using CivicLens.Publication.Contracts;

namespace CivicLens.PublicApi.Search;

/// <summary>
/// One indexed record revision: the manifest entry fields, the releases that hold it, and where its files are.
/// A snippet marks each match between U+E000 and U+E001. Only a search with text gives one.
/// </summary>
public sealed record RecordHit
{
    public required PublishedRecordKind Kind { get; init; }
    public required string RecordId { get; init; }
    public required int RevisionNumber { get; init; }
    public required string Headline { get; init; }
    public required DateTimeOffset FirstPublishedAtUtc { get; init; }
    public required PublishedOfficial[] Officials { get; init; }
    public required string[] IssueIds { get; init; }
    public required string[] PolicyAreaIds { get; init; }
    public string? SourceHost { get; init; }
    public DateOnly? EventDate { get; init; }
    public required int FirstRelease { get; init; }
    public int? LastRelease { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Snippet { get; init; }

    public string PagePath => PublicationProtocol.RecordPagePath(RecordId);
    public string DataPath => PublicationProtocol.RecordDataPath(RecordId);
}
