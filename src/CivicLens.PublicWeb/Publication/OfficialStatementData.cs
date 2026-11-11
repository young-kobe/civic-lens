using CivicLens.Publication.Contracts;

namespace CivicLens.PublicWeb.Publication;

/// <summary>
/// One statement of the official with the newest vote that a reviewer linked to it. <c>Cast</c> is null when the official has no cast on that vote.
/// <c>Label</c> and <c>VoteLabel</c> are the AI labels of the two headlines, and each is null when its headline is not AI text.
/// </summary>
public sealed record OfficialStatementData(string RecordId, DateOnly? StatementDate, string Headline, string? Label, string VoteRecordId, string VoteHeadline,
    string? VoteLabel, DateOnly VotedOn, PublishedChamber VoteChamber, int VoteRollCall, string? Cast);
