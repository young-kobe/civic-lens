using CivicLens.Publication.Contracts;

namespace CivicLens.PublicApi.DocumentChanges;

public sealed record DocumentChangeRow
{
    public required string RecordId { get; init; }
    public required int RevisionNumber { get; init; }
    public required string Headline { get; init; }
    public required string Summary { get; init; }
    public required PublishedTextOrigin TextOrigin { get; init; }
    public string? Label { get; init; }
    public string? Significance { get; init; }
    public string? Limits { get; init; }
    public required string Institution { get; init; }
    public DateOnly? ChangeDate { get; init; }
    public required string[] OfficialIds { get; init; }
    public required string[] IssueIds { get; init; }
}
