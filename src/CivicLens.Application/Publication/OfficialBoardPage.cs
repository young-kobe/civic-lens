using System.Collections.Immutable;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

/// <summary>
/// The lists of one board page, built from the figures of one Congress in the chamber of the board. That is the newest Congress while any member holds MinimumSplitVotes split votes in it. Until then it is the next older Congress that the summary rows hold. Members hold the officials
/// with a vote row for the chamber and party of the board, by name. The other counts are the officials that no board can show.
/// NewerCongress is the newest Congress of the chamber while the board keeps an older one, else null.
/// A list ranks only members with at least MinimumSplitVotes split votes cast.
/// </summary>
public sealed record OfficialBoardPage(OfficialBoard Board, int? Congress, ImmutableArray<BoardMember> Members, int WithoutVotes,
    int NotInCongress, int WithoutMajorParty, int? NewerCongress = null)
{
    public const int MinimumSplitVotes = 25;

    public ImmutableArray<BoardMember> Breakers =>
        [.. Ranked.OrderByDescending(member => member.BreakShare).ThenByDescending(member => member.Breaks).ThenBy(Name, StringComparer.Ordinal)];

    public ImmutableArray<BoardMember> Loyalists =>
        [.. Ranked.OrderBy(member => member.BreakShare).ThenByDescending(member => member.Cast).ThenBy(Name, StringComparer.Ordinal)];

    public ImmutableArray<BoardMember> TooFewSplitVotes => [.. Members.Where(member => member.Cast < MinimumSplitVotes)];

    public ImmutableArray<BoardMember> MostMissed =>
        [.. Members.Where(member => member.Missed is not null)
            .OrderByDescending(member => member.Missed!.Missed).ThenBy(Name, StringComparer.Ordinal)];

    public DateOnly? NewestRollCall => Members.Select(member => member.Missed?.Last).Max();

    private IEnumerable<BoardMember> Ranked => Members.Where(member => member.Cast >= MinimumSplitVotes);

    private static string Name(BoardMember member) => member.Official.Profile.Name;

    public static ImmutableArray<OfficialBoardPage> All(IReadOnlyList<OfficialListing> officials, IReadOnlyList<PublishedOfficialSummary> summaries)
    {
        var byId = summaries.ToDictionary(summary => summary.Id, StringComparer.Ordinal);
        var withoutVotes = summaries.Count(summary => summary.Congress is null);
        var withVotes = officials.Select(official => (Official: official, Summary: byId[official.Profile.Id])).Where(pair => pair.Summary.Congress is not null).ToList();
        return [.. OfficialBoard.All.Select(board => PageOf(board, withVotes, withoutVotes))];
    }

    private static OfficialBoardPage PageOf(OfficialBoard board, List<(OfficialListing Official, PublishedOfficialSummary Summary)> withVotes, int withoutVotes)
    {
        var inChamber = withVotes.Where(pair => AnyFigures(pair.Summary).Any(figures => HoldsChamber(figures, board.Chamber))).ToList();
        var (congress, newest) = ShownCongress(inChamber.Select(pair => pair.Summary).ToList(), board.Chamber);
        var shown = inChamber
            .Select(pair => (pair.Official, pair.Summary, Figures: congress is { } number ? pair.Summary.FiguresIn(number) : null))
            .Where(pair => pair.Figures is not null && HoldsChamber(pair.Figures, board.Chamber))
            .Select(pair => (pair.Official, pair.Summary, Figures: pair.Figures!))
            .ToList();
        return new(board, congress, MembersOf(board, shown), withoutVotes, inChamber.Count - shown.Count,
            shown.Count(pair => pair.Figures.PartyVotes.All(row => row.Chamber != board.Chamber)), newest > congress ? newest : null);
    }

    private static (int? Shown, int? Newest) ShownCongress(List<PublishedOfficialSummary> inChamber, PublishedChamber chamber)
    {
        var current = inChamber.Where(summary => HoldsChamber(summary.FiguresIn(summary.Congress!.Value)!, chamber)).ToList();
        var newest = current.Max(summary => summary.Congress);
        if (current.Any(summary => summary.Congress == newest && HasRankableMember(summary.PartyVotes, chamber))) return (newest, newest);
        var earlier = inChamber.SelectMany(AnyFigures).Where(figures => figures.Congress < newest && HoldsChamber(figures, chamber))
            .Select(figures => figures.Congress).DefaultIfEmpty().Max();
        return (earlier > 0 ? earlier : newest, newest);
    }

    private static string PagePathOf(OfficialListing official, int congress, PublishedOfficialSummary summary) =>
        congress == summary.Congress ? official.PagePath : PublicationProtocol.OfficialCongressPagePath(official.Profile.Id, congress);

    private static bool HasRankableMember(PublishedPartyVoteSummary[] partyVotes, PublishedChamber chamber) =>
        partyVotes.Any(row => row.Chamber == chamber && row.SplitVotesCast >= MinimumSplitVotes);

    private static IEnumerable<PublishedVoteFigures> AnyFigures(PublishedOfficialSummary summary) =>
        new[] { summary.FiguresIn(summary.Congress!.Value), summary.Previous }.OfType<PublishedVoteFigures>();

    private static bool HoldsChamber(PublishedVoteFigures figures, PublishedChamber chamber) =>
        figures.PartyVotes.Any(row => row.Chamber == chamber) || figures.Attendance.Any(row => row.Chamber == chamber);

    private static ImmutableArray<BoardMember> MembersOf(OfficialBoard board, List<(OfficialListing Official, PublishedOfficialSummary Summary, PublishedVoteFigures Figures)> shown) =>
    [.. shown
        .Select(pair => (pair.Official, pair.Summary, pair.Figures, Party: pair.Figures.PartyVotes.SingleOrDefault(row => row.Chamber == board.Chamber && row.Party == board.Party),
            Attendance: pair.Figures.Attendance.SingleOrDefault(row => row.Chamber == board.Chamber)))
        .Where(pair => pair.Party is not null)
        .Select(pair => new BoardMember(pair.Official, pair.Party!.SplitVotesCast, pair.Party.Breaks, pair.Attendance is { } attendance
            ? new MissedVotes(board.Chamber, attendance.Missed, attendance.Held, attendance.First, attendance.Last) : null, PagePathOf(pair.Official, pair.Figures.Congress, pair.Summary)))
        .OrderBy(Name, StringComparer.Ordinal)];
}
