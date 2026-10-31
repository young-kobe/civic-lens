using CivicLens.Application.Publication;
using CivicLens.Core.Relationships;
using CivicLens.Host.Components.Ui;
using CivicLens.Publication.Contracts;

namespace CivicLens.Host.Publication;

internal static class PublishedHomeCardView
{
    public const string PickedTitle = "Picked by code";

    public const string PickedLede = "Code picked these items from the published records. No AI wrote them. The same records give the same picks.";

    public const string BreaksTitle = "Voted against their party most often";

    public static readonly string BreaksRule = $"{PublishedOfficialView.SplitVoteMeaning} The card names one member for each chamber and party: the member with the highest share of split votes cast against most of the party. It counts only members with at least {BreakLeader.MinimumSplitVotes} split votes cast with Yea or Nay and at least one vote against the party. A vote against the party is not a mistake. The card shows counts and nothing more.";

    public static readonly string NoBreakLeaders = $"No member has {BreakLeader.MinimumSplitVotes} split votes cast with Yea or Nay and a vote against the party yet, so this card names no one.";

    public const string BreaksLeftOut = "A chamber and party with no line has no member who meets these rules.";

    public const string MethodLinkText = "How these picks are made";

    public const string ExecutiveActionsTitle = "Newest executive actions";

    public static string ClosestTitle(HomeHighlights highlights) =>
        $"Closest vote, {Moment.Day(highlights.WindowStart)} to {Moment.Day(highlights.WindowEnd)}";

    public static string NoVote(HomeHighlights highlights) =>
        $"No roll call vote with Yea and Nay counts is dated {Moment.Day(highlights.WindowStart)} to {Moment.Day(highlights.WindowEnd)} on this site.";

    public static string VoteGap(PublishedRecordEntry vote) => HomeHighlights.Gap(vote) switch
    {
        0 => $"Roll call on {Moment.Day(ReleaseRecordOrder.Day(vote))}. The Yea and Nay counts are equal.",
        var gap => $"Roll call on {Moment.Day(ReleaseRecordOrder.Day(vote))}. The Yea and Nay counts differ by {gap} {PublishedOfficialView.Noun(gap, "vote")}."
    };

    public static string VotePool(HomeHighlights highlights) => highlights.VotesInWindow == 1
        ? "This is the only roll call vote with counts in these dates."
        : $"The pick is the closest of {highlights.VotesInWindow} roll call votes with counts in these dates. The gap is the difference between the Yea count and the Nay count.";

    public static string Leader(BreakLeader leader) =>
        $"voted against most {PublishedOfficialView.PartyOf(leader.Chamber, leader.Party)} on {leader.Breaks} of {leader.Cast} split {PublishedOfficialView.Noun(leader.Cast, "vote")} in the {CongressCalendar.Ordinal(leader.Congress)} Congress.";

    public static bool LeavesOutGroups(HomeHighlights highlights) =>
        highlights.BreakLeaders.Length < Enum.GetValues<PublishedChamber>().Length * 2;

    public static string ExecutiveActionsCount(HomeHighlights highlights) =>
        $"The {highlights.ExecutiveActions.Length} newest of {highlights.ExecutiveActionTotal} executive {PublishedOfficialView.Noun(highlights.ExecutiveActionTotal, "action")} on this site. Each row shows its date.";

    public const string NoExecutiveActions = "No executive action is on this site yet.";
}
