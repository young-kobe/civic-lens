using CivicLens.Application.Publication;
using CivicLens.Publication.Contracts;
using CivicLens.PublicWeb.Publication;

namespace CivicLens.Host.Publication;

internal static class PublishedBreakCardView
{
    public const string Title = "Who broke from their party most";

    public const string RuleLabel = "How the card chooses these members";

    public const string BoardsLinkText = "See all four party boards";

    public static readonly string Rule = $"{PublishedOfficialView.SplitVoteMeaning} The card names one member for each chamber and party: the member with the highest share of split votes cast against most of the party. It counts only members with at least {OfficialBoardPage.MinimumSplitVotes} split votes cast as Yes or No and at least one vote against the party. A vote against the party is not a mistake. The card shows counts and nothing more.";

    public const string LeftOut = "A chamber and party with no line has no member with vote figures on this site who meets these rules.";

    public const string MethodLinkText = "How these choices are made";

    public static string Lede(IEnumerable<BreakLeader> leaders)
    {
        var congresses = leaders.Select(leader => leader.Congress).Distinct().OrderDescending().Select(CongressCalendar.Ordinal).ToList();
        return $"The share of split votes each member cast against most of their party, in the {string.Join(" and ", congresses)} {(congresses.Count == 1 ? "Congress" : "Congresses")}.";
    }

    public static string Votes(BreakLeader leader) => $"{leader.Member.Breaks} of {leader.Member.Cast} votes";

    public static string PartyAndState(BreakLeader leader, DateTimeOffset publishedAtUtc)
    {
        var counted = leader.Member.Official.Profile.Terms.Where(term => IsCountedWith(term, leader));
        if (PublishedOfficialView.LatestTerm(counted, publishedAtUtc) is not { } term) return "";
        return string.Join(", ", new[] { term.Party is null ? null : PartyLetters.LetterOf(term.Party), term.State is null ? null : UsStates.NameOf(term.State) }.OfType<string>());
    }

    private static bool IsCountedWith(PublishedOfficeTerm term, BreakLeader leader) =>
        term.Kind == (leader.Chamber == PublishedChamber.Senate ? PublishedOfficeKind.Senator : PublishedOfficeKind.Representative) &&
        (term.Caucus ?? term.Party) == leader.Party;

    public static bool LeavesOutGroups(HomeHighlights highlights) =>
        highlights.BreakLeaders.Length < OfficialBoard.All.Count;
}
