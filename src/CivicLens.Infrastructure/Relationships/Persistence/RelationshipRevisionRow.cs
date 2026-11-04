using CivicLens.Core.Relationships;
using CivicLens.Core.Review;
using CivicLens.Infrastructure.Review.Persistence;

namespace CivicLens.Infrastructure.Relationships.Persistence;

internal sealed class RelationshipRevisionRow : IReviewRevisionRow
{
    public string RecordId { get; set; } = string.Empty;
    public int RevisionNumber { get; set; }
    public string AuthorSubject { get; set; } = string.Empty;
    public ReviewAuthorKind AuthorKind { get; set; }
    public string? RunId { get; set; }
    public string RevisionJson { get; set; } = string.Empty;
    public long CreatedAtUtcTicks { get; set; }
    public string? StatementCandidateId { get; set; }
    public int? StatementRevisionNumber { get; set; }
    public RelationshipRecordKind? TargetKind { get; set; }
    public string? TargetActionRecordId { get; set; }
    public int? TargetActionRevisionNumber { get; set; }
    public string? TargetVoteRecordId { get; set; }
    public int? TargetVoteRevisionNumber { get; set; }
    public RelationshipMatchBasis? MatchBasis { get; set; }
    public string? MatchExtractionId { get; set; }
    public int? MatchStart { get; set; }
    public int? MatchLength { get; set; }
    public RelationshipType? Type { get; set; }
    public string? Reason { get; set; }
    public string? StatementQuoteExtractionId { get; set; }
    public int? StatementQuoteStart { get; set; }
    public int? StatementQuoteLength { get; set; }
    public string? TargetFactId { get; set; }
    public string? OfficialId { get; set; }
}
