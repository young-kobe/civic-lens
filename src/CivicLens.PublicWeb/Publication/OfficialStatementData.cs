using CivicLens.Publication.Contracts;

namespace CivicLens.PublicWeb.Publication;

/// <summary>
/// One statement of the official with the newest vote that a reviewer linked to it. <c>Cast</c> is null when the official has no cast on that vote.
/// Labels describe the origin of each headline. Typed origins select the model badge without inferring authorship from approval.
/// </summary>
public sealed record OfficialStatementData(string RecordId, DateOnly? StatementDate, string Headline, string? Label, string VoteRecordId, string VoteHeadline,
    string? VoteLabel, DateOnly VotedOn, PublishedChamber VoteChamber, int VoteRollCall, string? Cast)
{
    public PublishedTextOrigin? TextOrigin { get; init; }
    public PublishedTextOrigin? VoteTextOrigin { get; init; }
}
