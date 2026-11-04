using System.Collections.Immutable;
using CivicLens.Application.Publication;
using CivicLens.Host.Components.Ui;
using CivicLens.Publication.Contracts;

namespace CivicLens.Host.Publication;

internal static class PublishedVoteStripView
{
    public const string Title = "Every vote in this Congress";

    public static IReadOnlyList<PublishedOfficialVote> OldestFirst(ImmutableArray<PublishedOfficialVote> votes) =>
        [.. votes.OrderBy(vote => vote.VotedOn).ThenBy(vote => vote.Chamber).ThenBy(vote => vote.Session).ThenBy(vote => vote.RollCallNumber)];

    public static string Intro(IReadOnlyList<PublishedOfficialVote> votes) => votes.Count == 1
        ? $"One mark for the roll call of {Moment.Day(votes[0].VotedOn)}."
        : $"One mark for each of {votes.Count} roll calls, from {Moment.Day(votes[0].VotedOn)} on the left to {Moment.Day(votes[^1].VotedOn)} on the right.";

    public static string Description(IReadOnlyList<PublishedOfficialVote> votes)
    {
        var against = votes.Where(VotesWithParty.IsBreak).ToList();
        var total = $"{votes.Count} {PublishedOfficialView.Noun(votes.Count, "vote")}.";
        if (against.Count == 0) return $"{total} No mark shows a vote against most of their party on split votes.";
        var parties = against.Select(vote => (vote.Chamber, vote.Party)).Distinct().ToList();
        var party = parties.Count == 1 ? $"most {PublishedOfficialView.PartyOf(parties[0].Chamber, parties[0].Party)}" : "most of their party";
        var marks = against.Count == 1 ? "1 mark shows a vote" : $"{against.Count} marks show votes";
        return $"{total} {marks} against {party} on split votes.";
    }

    public static string MarkClass(PublishedOfficialVote vote)
    {
        var cast = PublishedOfficialVote.SideOf(vote.Cast) switch
        {
            PublishedPartyMajority.Yea => "vote-mark vote-mark-yes",
            PublishedPartyMajority.Nay => "vote-mark vote-mark-no",
            _ => "vote-mark vote-mark-other"
        };
        return VotesWithParty.IsBreak(vote) ? $"{cast} vote-mark-against" : cast;
    }
}
