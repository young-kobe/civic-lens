using CivicLens.Publication.Contracts;

namespace CivicLens.PublicWeb.Publication;

/// <summary>A vote linked by an approved relationship. The target revision, relationship evidence, and member cast come from the exact cited revisions;
/// the headline and name are current presentation metadata from the same release.</summary>
public sealed record StatementLinkedVoteData(
    string RelationshipRecordId,
    int RelationshipRevisionNumber,
    PublishedRelationshipType RelationshipType,
    int CitedStatementRevisionNumber,
    string Reason,
    PublishedTextOrigin ReasonTextOrigin,
    string? ReasonLabel,
    string Quote,
    string Fact,
    string? FactLabel,
    PublishedTextOrigin? FactTextOrigin,
    string VoteRecordId,
    int CitedVoteRevisionNumber,
    string VoteHeadline,
    PublishedTextOrigin VoteTextOrigin,
    string? VoteLabel,
    string LinkedOfficialId,
    string? LinkedOfficialName,
    string? CitedVoteCast,
    DateOnly VotedOn);
