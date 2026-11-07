using CivicLens.Publication.Contracts;

namespace CivicLens.PublicApi.Statements;

public sealed record StatementRow
{
    public required string RecordId { get; init; }
    public required int RevisionNumber { get; init; }
    public DateOnly? StatementDate { get; init; }
    public required string Headline { get; init; }
    public required string Summary { get; init; }
    public required PublishedTextOrigin TextOrigin { get; init; }
    public string? Label { get; init; }
    public required string[] SpeakerIds { get; init; }
    public required string[] IssueIds { get; init; }
}
