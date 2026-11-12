using CivicLens.Publication.Contracts;

namespace CivicLens.PublicWeb.Publication;

/// <summary>The one owner of the words of the vote map: its heading, view names, notes, and the name of each seat.</summary>
public static class VoteMapWords
{
    public const string Heading = "Map of the vote";
    public const string LegendHeading = "What the colors count";
    public const string ViewGroup = "View";
    public const string ColorGroup = "Color";
    public const string ByVote = "By vote";
    public const string ByParty = "By party";
    public const string OpenFullMap = "Open the full map";
    public const string BackToVote = "Back to the vote page";
    public const string RecordedVotesOnly = "This map shows recorded votes only. It makes no predictions and gives no ratings.";
    public const string SourceFold = "Where the data comes from";
    public const string ShapesTerm = "Shapes";
    public static readonly string ShapesSource = $"Census Bureau 2024 cartographic boundary file for the {CongressCalendar.Ordinal(VoteMapGeometry.Congress)} Congress, at 1:20,000,000 scale, simplified more for the web. Alaska and Hawaii sit in boxes. Use the shapes to see the pattern, not to find an address.";
    public const string TileView = "One tile per seat";
    public const string TileNote = "Each seat casts one vote, but large rural districts take up more room on a map. The tile view gives every seat the same size.";
    public const string TableFold = "See the table";
    public const string DelegatesHeading = "Delegates";
    public const string DelegatesNote = "Delegates show as circles. They are not in the map or the tiles. The counts include them.";

    public const string NoMapTitle = "No map for this vote";
    public const string NoMap = "The record does not hold the seats that a map needs. The vote page lists every tracked member.";

    /// <summary>"How the House voted: roll call 308, H.R. 5334". The bill number comes from the one bill number owner.</summary>
    public static string PageTitle(VotePageData vote) => vote.BillType is { } type && vote.BillNumber is { } number
        ? $"How the {vote.Chamber} voted: roll call {vote.RollCall}, {BillNumbers.Of(type, number)}"
        : $"How the {vote.Chamber} voted: roll call {vote.RollCall}";

    public static string ShapeView(PublishedChamber chamber) => chamber == PublishedChamber.Senate ? "Map of states" : "Map of districts";

    public static string Intro(PublishedChamber chamber) => chamber == PublishedChamber.Senate
        ? "Each state holds its two Senate seats, filled by how each senator voted. The first senator in the record takes the left half and the first tile."
        : "Each shape is one House seat, filled by how its member voted.";

    /// <summary>The label of the shapes alone, as Home draws them: the intro, then the count of each mark in use.</summary>
    public static string CompactDescription(PublishedChamber chamber, IEnumerable<(VoteMapMark Mark, int Count)> legend) =>
        $"{Heading}. {Intro(chamber).TrimEnd('.')}: {string.Join(", ", legend.Select(entry => $"{entry.Count} {VoteMapMarks.Label(entry.Mark)}"))}.";

    /// <summary>The note under the legend heading in the party color mode.</summary>
    public static string PartyLegendNote(int total) =>
        $"Color shows the member's party. A solid fill means Yes, stripes mean No, and dots mean the member did not vote. The counts add up to {total} seats.";

    public static string Description(PublishedChamber chamber) =>
        $"{Heading} of the {chamber} seats. The legend and the table below hold the same facts.";

    public static string LegendNote(PublishedChamber chamber, int rollCall, int total, bool withDelegates) =>
        $"Each mark counts {chamber} seats{(withDelegates ? " and delegate circles" : "")} on roll call {rollCall} by how the member voted. The counts add up to {total}.";

    public static string Unplaced(int count) => count == 1
        ? "1 tracked member has no seat in the record, so the map leaves them out."
        : $"{count} tracked members have no seat in the record, so the map leaves them out.";

    public static string TableCaption(PublishedChamber chamber, int rollCall) => $"How each {chamber} seat voted on roll call {rollCall}";

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
        $"{member.Name}, {member.PartyLabel}, {PlainVoteWords.Cast(member.Cast)}";
}
