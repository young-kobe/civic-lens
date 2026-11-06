using CivicLens.Publication.Contracts;

namespace CivicLens.Infrastructure.Publication.Persistence;

internal sealed class PublishedEntryRow
{
    public string Kind { get; set; } = string.Empty;
    public string RecordId { get; set; } = string.Empty;
    public int RevisionNumber { get; set; }
    public string? ActionRecordId { get; set; }
    public int? ActionRevisionNumber { get; set; }
    public string? BriefRecordId { get; set; }
    public int? BriefRevisionNumber { get; set; }
    public string? ExecutiveActionRecordId { get; set; }
    public int? ExecutiveActionRevisionNumber { get; set; }
    public string? RelationshipRecordId { get; set; }
    public int? RelationshipRevisionNumber { get; set; }
    public string? StatementCandidateId { get; set; }
    public int? StatementRevisionNumber { get; set; }
    public string? DocumentChangeDraftId { get; set; }
    public int? DocumentChangeRevisionNumber { get; set; }
    public string? VoteRecordId { get; set; }
    public int? VoteRevisionNumber { get; set; }
    public string Headline { get; set; } = string.Empty;
    public string? SourceHost { get; set; }
    public DateOnly? EventDate { get; set; }
    public string? BillTitle { get; set; }
    public PublishedRelationshipType? LinkType { get; set; }
    public string? StatementRecordId { get; set; }
    public string? StatementKind { get; set; }
    public string? TargetKind { get; set; }
    public string? TargetRecordId { get; set; }
    public PublishedExecutiveActionKind? DocumentType { get; set; }
    public string? BillLine { get; set; }
    public int? Congress { get; set; }
    public PublishedApprover? ApprovedBy { get; set; }
}
