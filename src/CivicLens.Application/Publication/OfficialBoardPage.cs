using System.Collections.Immutable;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

/// <summary>
/// The lists of one board page, built from the summary rows of the newest Congress that any official has in the chamber of the board. Members hold the officials
/// with a vote row for the chamber and party of the board, by name. The other counts are the officials that no board can show.
/// A list ranks only members with at least MinimumSplitVotes split votes cast.
/// </summary>
public sealed record OfficialBoardPage(OfficialBoard Board, int? Congress, ImmutableArray<BoardMember> Members, int WithoutVotes,
    int InEarlierCongress, int WithoutMajorParty)
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
        return [.. OfficialBoard.All.Select(board => PageOf(board, withVotes.Where(pair => HoldsChamber(pair.Summary, board.Chamber)).ToList(), withoutVotes))];
    }

    private static OfficialBoardPage PageOf(OfficialBoard board, List<(OfficialListing Official, PublishedOfficialSummary Summary)> inChamber, int withoutVotes)
    {
        var congress = inChamber.Max(pair => pair.Summary.Congress);
        var current = inChamber.Where(pair => pair.Summary.Congress == congress).ToList();
        return new(board, congress, MembersOf(board, current), withoutVotes, inChamber.Count - current.Count,
            current.Count(pair => pair.Summary.PartyVotes.All(row => row.Chamber != board.Chamber)));
    }

    private static bool HoldsChamber(PublishedOfficialSummary summary, PublishedChamber chamber) =>
        summary.PartyVotes.Any(row => row.Chamber == chamber) || summary.Attendance.Any(row => row.Chamber == chamber);

    private static ImmutableArray<BoardMember> MembersOf(OfficialBoard board, List<(OfficialListing Official, PublishedOfficialSummary Summary)> current) =>
    [.. current
        .Select(pair => (pair.Official, Party: pair.Summary.PartyVotes.SingleOrDefault(row => row.Chamber == board.Chamber && row.Party == board.Party),
            Attendance: pair.Summary.Attendance.SingleOrDefault(row => row.Chamber == board.Chamber)))
        .Where(pair => pair.Party is not null)
        .Select(pair => new BoardMember(pair.Official, pair.Party!.SplitVotesCast, pair.Party.Breaks, pair.Attendance is { } attendance
            ? new MissedVotes(board.Chamber, attendance.Missed, attendance.Held, attendance.First, attendance.Last) : null))
        .OrderBy(Name, StringComparer.Ordinal)];
}
