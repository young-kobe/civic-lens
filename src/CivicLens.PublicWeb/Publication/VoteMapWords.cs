using CivicLens.Publication.Contracts;

namespace CivicLens.PublicWeb.Publication;

/// <summary>The one owner of the words of the vote map: its heading, view names, notes, and the name of each seat.</summary>
public static class VoteMapWords
{
    public const string Heading = "Map of the vote";
    public const string LegendHeading = "What the colors count";
    public const string ViewGroup = "View";
    public const string TileView = "One tile per seat";
    public const string TileNote = "Each seat casts one vote, but large rural districts take up more room on a map. The tile view gives every seat the same size.";
    public const string TableFold = "See the table";
    public const string DelegatesHeading = "Delegates";
    public const string DelegatesNote = "Delegates show as circles. They are not in the map or the tiles. The counts include them.";

    public static string ShapeView(PublishedChamber chamber) => chamber == PublishedChamber.Senate ? "Map of states" : "Map of districts";

    public static string Intro(PublishedChamber chamber) => chamber == PublishedChamber.Senate
        ? "Each state holds its two Senate seats, filled by how each senator voted. The first senator in the record takes the left half and the first tile."
        : "Each shape is one House seat, filled by how its member voted.";

    public static string Description(PublishedChamber chamber) =>
        $"{Heading} of the {chamber} seats. The legend and the table below hold the same facts.";

    public static string Unplaced(int count) => count == 1
        ? "1 tracked member has no seat in the record, so the map leaves them out."
        : $"{count} tracked members have no seat in the record, so the map leaves them out.";

    public static string TableCaption(PublishedChamber chamber) => $"How each {chamber} seat voted";

    /// <summary>"Massachusetts district 7", "Alaska at large", or the state name alone for a Senate seat.</summary>
    public static string SeatName(VoteMapSeat seat) => seat.District switch
    {
        null => UsStates.NameOf(seat.State),
        0 => $"{UsStates.NameOf(seat.State)} at large",
        { } district => $"{UsStates.NameOf(seat.State)} district {district}"
    };

    /// <summary>"Puerto Rico, Resident Commissioner".</summary>
    public static string DelegateSeat(VoteMemberData member) =>
        $"{UsStates.NameOf(member.State!)}, {UsStates.HouseOfficeByNonStateCode[member.State!]}";

    /// <summary>The hover text of a seat: the seat, then the member, party, and cast, or the no-member words.</summary>
    public static string Describe(VoteMapSeat seat) =>
        seat.Member is { } member ? $"{SeatName(seat)}: {Member(member)}" : $"{SeatName(seat)}: {VoteMapMarks.NoMember}";

    public static string Member(VoteMemberData member) =>
        $"{member.Name}, {member.Party ?? VoteWords.NoKnownParty}, {PlainVoteWords.Cast(member.Cast)}";
}
