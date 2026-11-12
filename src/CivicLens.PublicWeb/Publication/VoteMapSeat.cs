namespace CivicLens.PublicWeb.Publication;

/// <summary>
/// One seat on the vote map. A null member means that the vote record has no member row for the seat.
/// A House seat has its district shape. A Senate seat has the half of its state: 0 for the first senator in position order, 1 for the second.
/// </summary>
public sealed record VoteMapSeat(string State, int? District, VoteMemberData? Member, VoteMapTile Tile, string? Path, int Half)
{
    public VoteMapMark Mark => VoteMapMarks.Of(Member);
}
