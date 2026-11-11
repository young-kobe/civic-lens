using CivicLens.Publication.Contracts;

namespace CivicLens.PublicWeb.Publication;

/// <summary>One statement of the official with the newest vote that a reviewer linked to it. <c>Cast</c> is null when the official has no cast on that vote.</summary>
public sealed record OfficialStatementData(string RecordId, DateOnly? StatementDate, string Headline, string VoteRecordId, string VoteHeadline,
    DateOnly VotedOn, PublishedChamber VoteChamber, int VoteRollCall, string? Cast);
