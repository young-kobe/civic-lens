using CivicLens.Application.Publication;
using CivicLens.Core.Relationships;
using CivicLens.Host.Components.Ui;
using CivicLens.Publication.Contracts;

namespace CivicLens.Host.Publication;

internal static class PublishedHomeCardView
{
    public const string PickedTitle = "Picked by code";

    public const string PickedLede = "Code picks these items from the published records. The same records on the same release day give the same picks. A vote headline can be an AI summary of the official record, and its vote page says so.";

    public const string BreaksTitle = "Voted against their party most often";

    public static readonly string BreaksRule = $"{PublishedOfficialView.SplitVoteMeaning} The card names one member for each chamber and party: the member with the highest share of split votes cast against most of the party. It counts only members with at least {OfficialBoardPage.MinimumSplitVotes} split votes cast with Yea or Nay and at least one vote against the party. A vote against the party is not a mistake. The card shows counts and nothing more.";

    public static readonly string NoBreakLeaders = $"No member with vote figures on this site has {OfficialBoardPage.MinimumSplitVotes} split votes cast with Yea or Nay and a vote against the party yet, so this card names no one.";

    public const string BreaksLeftOut = "A chamber and party with no line has no member with vote figures on this site who meets these rules.";

    public const string MethodLinkText = "How these picks are made";

    public const string ExecutiveActionsTitle = "Newest executive actions";

    public static string ClosestTitle(HomeHighlights highlights) =>
        $"Closest vote, {Moment.Day(highlights.WindowStart)} to {Moment.Day(highlights.WindowEnd)}";

    public const string ClosestRule = "The card looks at the substantive roll call votes of the House and the Senate in which both the Democrats and the Republicans voted. It leaves out procedural votes, such as rules and motions. The gap ignores thresholds such as 60 votes in the Senate or two thirds of a chamber.";

    public static string NoVote(HomeHighlights highlights) =>
        $"No substantive roll call vote with Yea and Nay counts is dated {Moment.Day(highlights.WindowStart)} to {Moment.Day(highlights.WindowEnd)} on this site.";

    public static string VoteGap(PublishedRecordEntry vote) => HomeHighlights.Gap(vote) switch
    {
        0 => $"Roll call on {Moment.Day(ReleaseRecordOrder.Day(vote))}. The Yea and Nay counts are equal.",
        var gap => $"Roll call on {Moment.Day(ReleaseRecordOrder.Day(vote))}. The Yea and Nay counts differ by {gap} {PublishedOfficialView.Noun(gap, "vote")}."
    };

    public static string VotePool(HomeHighlights highlights) => highlights.VotesInWindow == 1
        ? "This is the only substantive roll call vote with counts in these dates."
        : $"The pick is the closest of {highlights.VotesInWindow} substantive roll call votes with counts in these dates. The gap is the difference between the Yea count and the Nay count.";

    public static string Leader(BreakLeader leader) =>
        $"voted against most {PublishedOfficialView.PartyOf(leader.Chamber, leader.Party)} on {leader.Breaks} of the {leader.Cast} split {PublishedOfficialView.Noun(leader.Cast, "vote")} they cast with Yea or Nay in the {CongressCalendar.Ordinal(leader.Congress)} Congress.";

    public static bool LeavesOutGroups(HomeHighlights highlights) =>
        highlights.BreakLeaders.Length < Enum.GetValues<PublishedChamber>().Length * 2;

    public static string ExecutiveActionsCount(HomeHighlights highlights) => "Picked by code: " + (highlights.ExecutiveActionTotal switch
    {
        1 => "the only executive action on this site.",
        var total when total == highlights.ExecutiveActions.Length => $"all {total} executive actions on this site, newest first.",
        var total => $"the {highlights.ExecutiveActions.Length} newest of {total} executive actions on this site."
    }) + " Each row shows its date.";

    public const string NoExecutiveActions = "No executive action is on this site yet.";
}
