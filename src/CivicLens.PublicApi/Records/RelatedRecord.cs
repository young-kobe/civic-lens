using CivicLens.Publication.Contracts;

namespace CivicLens.PublicApi.Records;

/// <summary>One row of the view <c>public_api.record_links</c> as the page of a record shows it: the record at the other end of an approved relationship.
/// <c>StatementDate</c> is the date of the statement of the relationship, and it is null when the statement has no date. <c>Headline</c> is the current headline of the other record. <c>TextOrigin</c> and <c>Label</c> say who wrote it, as on a bill row, and <c>Label</c> is null for person text. <c>LinkRecordId</c> names the relationship record that explains the link. <c>Reason</c> comes from the exact published relationship revision, and is null when that revision is unavailable.</summary>
public sealed record RelatedRecord
{
    public required PublishedRecordKind Kind { get; init; }
    public required string RecordId { get; init; }
    public required string Headline { get; init; }
    public required PublishedTextOrigin TextOrigin { get; init; }
    public string? Label { get; init; }
    public required PublishedRelationshipType LinkType { get; init; }
    public DateOnly? StatementDate { get; init; }
    public required string LinkRecordId { get; init; }
    public string? Reason { get; init; }
}
