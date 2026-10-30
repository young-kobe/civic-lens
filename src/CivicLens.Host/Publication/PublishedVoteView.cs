using System.Globalization;
using CivicLens.Application.Publication;
using CivicLens.Core.Votes;
using CivicLens.Host.Actions;
using CivicLens.Host.Collection;
using CivicLens.Host.Components.Evidence;
using CivicLens.Host.Components.Ui;
using CivicLens.Host.Votes;
using CivicLens.Publication.Contracts;

namespace CivicLens.Host.Publication;

internal static class PublishedVoteView
{
    public static IReadOnlyList<Fact> Facts(PublishedVote vote, PublicationCatalog catalog) =>
    [
        new("Voted on", Moment.Iso(vote.VotedOn)),
        new("Kind", KindValue(vote)) { TermKey = PublishedRecordLabels.VoteKindTermKey(vote.Kind) },
        new("Result", vote.Result),
        new("Totals", Totals(vote)),
        new("Chamber", vote.Chamber.ToString()),
        new("Roll call", $"{vote.RollCallNumber}, Congress {vote.Congress}, session {vote.Session}"),
        new("Data source", PublishedRecordLabels.VoteSource(vote.Source)),
        Fact.LinkList("Official roll call page", [new FactLink($"Roll call on {VoteLabels.RollCallSite(ChamberOf(vote))}",
            SourcePages.RollCall(ChamberOf(vote), vote.Congress, vote.Session, vote.RollCallNumber))]),
        .. PublishedRecordView.Issues("Tracked issues", vote.TrackedIssueIds, catalog),
        new("Published", $"From the official record, checked {Moment.Full(vote.ApprovedAtUtc)}; first published {Moment.Full(vote.FirstPublishedAtUtc)}"),
        .. PublishedRecordLabels.Wording(vote.TextOrigin),
        new("Revision", vote.RevisionNumber.ToString(CultureInfo.InvariantCulture))
    ];

    private static string KindValue(PublishedVote vote) =>
        vote.Kind != PublishedVoteKind.Other ? PublishedRecordLabels.VoteKind(vote.Kind)
            : vote.KindCitation is null ? $"{PublishedRecordLabels.OtherQuestion}. The source gives no question."
            : $"{PublishedRecordLabels.OtherQuestion}. The Kind quote in the Result panel gives the question.";

    public static string Totals(PublishedVote vote)
    {
        var body = MemberNoun(vote) + "s";
        return vote.Totals is { } totals
            ? $"Casts of all {body}: Yea {totals.Yea}, Nay {totals.Nay}, Present {totals.Present}, Not voting {totals.NotVoting}"
            : $"Votes for each candidate from all {body}: {string.Join(", ", CandidateTotals(vote))}";
    }

    private static string MemberNoun(PublishedVote vote) => vote.Chamber == PublishedChamber.Senate ? "senator" : "House member";

    private static IEnumerable<string> CandidateTotals(PublishedVote vote) =>
        vote.Facts.Where(fact => fact.Text.StartsWith(PublishedVote.CandidateTotalPrefix, StringComparison.Ordinal))
            .Select(fact => fact.Text[PublishedVote.CandidateTotalPrefix.Length..].Replace(": ", " ", StringComparison.Ordinal));

    public static string Caveat(PublishedVote vote) =>
        "This record lists only the officials that Civic Lens tracks. " + vote.Source switch
        {
            PublishedVoteSource.Voteview => "Voteview stores each vote as a number code. Civic Lens shows that code as a word such as Yea or Nay. The Senate roll call may use other words. Voteview does not list a Vice President tie-break vote.",
            _ => $"Each cast is the wording of the {(vote.Chamber == PublishedChamber.Senate ? "Senate" : "House Clerk")} roll call at the observation time of its source."
        };

    public static string SavedData(PublishedVote vote) => vote.Source switch
    {
        PublishedVoteSource.Voteview => "Voteview files",
        PublishedVoteSource.CongressGov => "Congress.gov API page",
        _ => throw new ArgumentOutOfRangeException(nameof(vote), vote.Source, "Vote source has no saved data name.")
    };

    private static Chamber ChamberOf(PublishedVote vote) => vote.Chamber == PublishedChamber.Senate ? Chamber.Senate : Chamber.House;

    public static string? Bill(PublishedVote vote) =>
        vote.BillType is null ? null : ActionLabels.Bill(vote.Congress, vote.BillType, vote.BillNumber!.Value);

    public static IEnumerable<PublishedVoteFact> ResultFacts(PublishedVote vote) =>
        vote.Facts.Where(fact => fact.Citation != vote.VotedOnCitation && fact.Citation != vote.KindCitation);

    public static IEnumerable<CastGroup> CastGroups(PublishedVote vote) =>
        vote.Members.GroupBy(member => member.Cast)
            .OrderBy(group => CastRank(group.Key))
            .ThenByDescending(group => CastRank(group.Key) == CandidateRank ? group.Count() : 0)
            .ThenBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => Heading(vote, group.Key, group.Count(), ByName(group)));

    private static PublishedMemberVote[] ByName(IEnumerable<PublishedMemberVote> members) =>
        [.. members.OrderBy(member => member.Official.Name, StringComparer.OrdinalIgnoreCase).ThenBy(member => member.Official.Id, StringComparer.Ordinal)];

    public static VoteBadge? Badge(PublishedVote vote)
    {
        var democrats = $"most {PartyOf(vote, PublishedPartyMajority.Democrat)}";
        var republicans = $"most {PartyOf(vote, PublishedPartyMajority.Republican)}";
        var democratSide = vote.MajorityOf(PublishedPartyMajority.Democrat);
        const string cast = "who cast Yea or Nay";
        if (vote.IsSplit())
            return new("Split vote", $"Split vote: {democrats} {cast} voted {democratSide}, and {republicans} {cast} voted {vote.MajorityOf(PublishedPartyMajority.Republican)}.", PartyCounts(vote));
        return vote.PartiesAgree() ? new("Both parties agreed", $"Both parties agreed: {democrats} and {republicans} {cast} voted {democratSide}.", PartyCounts(vote)) : null;
    }

    private static string PartyCounts(PublishedVote vote) =>
        string.Join(' ', new[] { PublishedPartyMajority.Democrat, PublishedPartyMajority.Republican }.Select(party => PartyCount(vote, party))) +
        " These counts include every member of the party, tracked or not.";

    private static string PartyCount(PublishedVote vote, string party)
    {
        var count = vote.PartyMajorities.Single(majority => majority.Party == party);
        return $"{PartyOf(vote, party)}: {count.YeaCount} Yea, {count.NayCount} Nay.";
    }

    public const string BreakMeaning = "A member is listed when they cast Yea or Nay against the cast of most members of their own party on this vote. A current independent counts with the party they caucus with. A former independent counts with no party. A member with another party, or with no known party, is not listed. Present and Not voting never count as a break.";

    public static IEnumerable<PartyBreakers> Breakers(PublishedVote vote) =>
        vote.IsSplit() ? new[] { PublishedPartyMajority.Democrat, PublishedPartyMajority.Republican }.Select(party => BreakersOf(vote, party)) : [];

    private static PartyBreakers BreakersOf(PublishedVote vote, string party)
    {
        var members = ByName(vote.Members.Where(member => member.Party == party &&
            vote.MajorityOf(party) is { } majority && VotesWithParty.IsAgainstPartyMajority(member.Party, member.Cast, majority)));
        var name = PartyOf(vote, party);
        return new($"{name}: {members.Length} tracked {PublishedOfficialView.Noun(members.Length, "member")} voted against most of the party",
            $"No tracked {PublishedOfficialView.PartyMemberOf(vote.Chamber, party)} voted against most {name}.", members);
    }

    private static string PartyOf(PublishedVote vote, string party) => PublishedOfficialView.PartyOf(vote.Chamber, party);

    private const int CandidateRank = 2;

    private static int CastRank(string cast) => cast switch
    {
        PublishedOfficialVote.NotVoting => 4,
        PublishedRecordLabels.Present => 3,
        _ => PublishedOfficialVote.SideOf(cast) switch
        {
            PublishedPartyMajority.Yea => 0,
            PublishedPartyMajority.Nay => 1,
            _ => CandidateRank
        }
    };

    private static CastGroup Heading(PublishedVote vote, string cast, int count, PublishedMemberVote[] members)
    {
        var (lead, word) = cast switch
        {
            PublishedOfficialVote.NotVoting => ("", "Did not vote"),
            PublishedRecordLabels.Present => ("Voted ", PublishedRecordLabels.Present),
            _ => CastRank(cast) == CandidateRank ? ("Voted for ", cast) : ("Voted ", cast)
        };
        return new(lead, word, cast, $" ({count} tracked {PublishedOfficialView.Noun(count, MemberNoun(vote))})", members);
    }

    public static string Anchor(PublishedVote vote, PublishedCitation citation) =>
        PublishedRecordView.CitationAnchor(vote.Citations().TakeWhile(item => item != citation).Count());

    public static IReadOnlyList<CitedRange> RangesIn(PublishedVote vote, PublishedDocumentVersion source) =>
    [.. vote.Citations().Select((citation, index) => (citation, anchor: PublishedRecordView.CitationAnchor(index)))
        .DistinctBy(item => item.citation).Where(item => item.citation.ExtractionId == source.ExtractionId)
        .Select(item => new CitedRange(item.anchor, item.citation.Start, item.citation.Length))];
}
