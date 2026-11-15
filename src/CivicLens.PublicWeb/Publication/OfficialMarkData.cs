using CivicLens.Publication.Contracts;

namespace CivicLens.PublicWeb.Publication;

/// <summary>
/// One square of the vote strip: one roll call of the official, with the source cast. <c>BreaksParty</c> is the view column <c>breaks_party</c>:
/// true when the cast went against most of the party, which <c>Party</c> names.
/// </summary>
public sealed record OfficialMarkData(string RecordId, DateOnly VotedOn, PublishedChamber Chamber, int RollCall, string Cast, bool BreaksParty, string? Party)
{
    public VoteMapMark Mark => VoteMapMarks.Of(Cast);
}
