using System.Globalization;
using CivicLens.Application.Publication;
using CivicLens.Core.Votes;
using CivicLens.Host.Actions;
using CivicLens.Host.Collection;
using CivicLens.Host.Votes;
using CivicLens.Publication.Contracts;
using CivicLens.PublicWeb.Components.Evidence;
using CivicLens.PublicWeb.Publication;
using CivicLens.PublicWeb.Components.Ui;

namespace CivicLens.Host.Publication;

internal static class PublishedVoteView
{
    public static string RollCall(PublishedChamber chamber, int rollCallNumber) => $"{chamber} roll call {rollCallNumber}";

    public static IReadOnlyList<Fact> Facts(PublishedVote vote, PublicationCatalog catalog) =>
    [
        .. VoteFacts.Cited(VoteFactsSource.Of(vote), citation => Anchor(vote, citation)),
        .. VoteFacts.Identity(vote.Chamber, vote.RollCallNumber, vote.Congress, vote.Session, vote.Source),
        Fact.LinkList("Official roll call page", [new FactLink($"Roll call on {VoteLabels.RollCallSite(ChamberOf(vote))}",
            SourcePages.RollCall(ChamberOf(vote), vote.Congress, vote.Session, vote.RollCallNumber))]),
        .. PublishedRecordView.Issues("Tracked issues", vote.TrackedIssueIds, catalog),
        new("Published", $"From the official record, checked {Moment.Full(vote.ApprovedAtUtc)}; first published {Moment.Full(vote.FirstPublishedAtUtc)}"),
        .. PublishedRecordLabels.Wording(vote.TextOrigin),
        new("Revision", vote.RevisionNumber.ToString(CultureInfo.InvariantCulture))
    ];

    public static (string Yes, string No, string NextStep)? Wording(PublishedVote vote) => VoteWording.Of(vote);

    public static string Caveat(PublishedVote vote) =>
        VoteWords.TrackedOnly + " " + vote.Source switch
        {
            PublishedVoteSource.Voteview => "Voteview stores each vote as a number code. Civic Lens shows that code as a word such as Yes (Yea) or No (Nay). The Senate roll call may use other words. Voteview does not list a Vice President tie-break vote.",
            _ => $"Each cast is the wording of the {(vote.Chamber == PublishedChamber.Senate ? "Senate" : "House Clerk")} roll call at the observation time of its source."
        };

    private static Chamber ChamberOf(PublishedVote vote) => vote.Chamber == PublishedChamber.Senate ? Chamber.Senate : Chamber.House;

    public static string? Bill(PublishedVote vote) =>
        vote.BillType is null ? null : ActionLabels.Bill(vote.Congress, vote.BillType, vote.BillNumber!.Value);

    public static IEnumerable<CastGroup> CastGroups(PublishedVote vote) =>
        vote.Members.GroupBy(member => member.Cast)
            .OrderBy(group => CastRank(group.Key))
            .ThenByDescending(group => PublishedOfficialVote.IsCandidate(group.Key) ? group.Count() : 0)
            .ThenBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => Heading(vote, group.Key, group.Count(), ByName(group)));

    private static PublishedMemberVote[] ByName(IEnumerable<PublishedMemberVote> members) =>
        [.. members.OrderBy(member => member.Official.Name, StringComparer.OrdinalIgnoreCase).ThenBy(member => member.Official.Id, StringComparer.Ordinal)];

    public static (string Text, string Meaning)? ListBadge(PublishedRecordEntry entry) => VoteWords.Badge(entry.Alignment);

    public static string? ListYeaNay(PublishedRecordEntry entry) =>
        entry.YeaCount is { } yea && entry.NayCount is { } nay ? PlainVoteWords.YesNo(yea, nay) : null;

    private const string CountsWho = "of all members of the chamber who voted Yes or No";

    public static string? ListCounts(PublishedRecordEntry entry) =>
        ListYeaNay(entry) is { } counts ? $"Casts {CountsWho}: {counts}." : null;

    public static IEnumerable<string> ListNotes(IEnumerable<PublishedRecordEntry> entries, bool defineBadges, ReleaseBillLines billLines)
    {
        var votes = entries.Where(entry => entry.Kind == PublishedRecordKind.Vote).ToArray();
        if (defineBadges && votes.Any(entry => entry.Alignment == PublishedVoteAlignment.Split)) yield return VoteWords.SplitMeaning;
        if (defineBadges && votes.Any(entry => entry.Alignment == PublishedVoteAlignment.PartiesAgree)) yield return VoteWords.PartiesAgreedMeaning;
        if (votes.Any(entry => ListYeaNay(entry) is not null)) yield return $"Yes and No counts are casts {CountsWho}.";
        if (billLines.AnyOf(entries)) yield return PublishedWording.BillLineSource;
    }

    public static VoteBadge? Badge(PublishedVote vote)
    {
        var democrats = $"most {PartyOf(vote, PublishedPartyMajority.Democrat)}";
        var republicans = $"most {PartyOf(vote, PublishedPartyMajority.Republican)}";
        const string cast = "who cast Yes or No";
        if (vote.IsSplit())
            return new(VoteWords.SplitBadge, $"{VoteWords.SplitBadge}: {democrats} {cast} voted {SideWord(vote, PublishedPartyMajority.Democrat)}, and {republicans} {cast} voted {SideWord(vote, PublishedPartyMajority.Republican)}.", PartyCounts(vote));
        return vote.PartiesAgree() ? new(VoteWords.PartiesAgreedBadge, $"{VoteWords.PartiesAgreedBadge}: {democrats} and {republicans} {cast} voted {SideWord(vote, PublishedPartyMajority.Democrat)}.", PartyCounts(vote)) : null;
    }

    private static string SideWord(PublishedVote vote, string party) =>
        PlainVoteWords.CastWord(vote.MajorityOf(party) ?? "") ?? throw new InvalidOperationException("A vote with a badge has a majority for each party.");

    private static string PartyCounts(PublishedVote vote) =>
        string.Join(' ', new[] { PublishedPartyMajority.Democrat, PublishedPartyMajority.Republican }.Select(party => PartyCount(vote, party))) +
        " " + VoteWords.PartyCountsNote;

    private static string PartyCount(PublishedVote vote, string party)
    {
        var count = vote.PartyMajorities.Single(majority => majority.Party == party);
        return $"{PartyOf(vote, party)}: {count.YeaCount} Yes, {count.NayCount} No.";
    }

    public static IEnumerable<PartyBreakers> Breakers(PublishedVote vote) =>
        vote.IsSplit() ? new[] { PublishedPartyMajority.Democrat, PublishedPartyMajority.Republican }.Select(party => BreakersOf(vote, party)) : [];

    private static PartyBreakers BreakersOf(PublishedVote vote, string party)
    {
        var members = ByName(vote.Members.Where(member => member.Party == party &&
            vote.MajorityOf(party) is { } majority && PublishedPartyMajority.IsAgainstPartyMajority(member.Party, member.Cast, majority)));
        var name = PartyOf(vote, party);
        return new($"{name}: {VoteBreakers.Headline(members.Length)}", $"{name}: {VoteBreakers.None}", members);
    }

    private static string PartyOf(PublishedVote vote, string party) => PublishedOfficialView.PartyOf(vote.Chamber, party);

    private static int CastRank(string cast) => cast switch
    {
        PublishedOfficialVote.NotVoting => 4,
        PublishedOfficialVote.Present => 3,
        _ when PublishedOfficialVote.IsCandidate(cast) => 2,
        _ => PublishedOfficialVote.SideOf(cast) == PublishedPartyMajority.Yea ? 0 : 1
    };

    private static CastGroup Heading(PublishedVote vote, string cast, int count, PublishedMemberVote[] members)
    {
        var (lead, word) = cast switch
        {
            PublishedOfficialVote.NotVoting => ("", "Did not vote"),
            PublishedOfficialVote.Present => ("Voted ", PlainVoteWords.Present),
            _ when PublishedOfficialVote.IsCandidate(cast) => ("Voted for ", cast),
            _ => ("Voted ", PlainVoteWords.Cast(cast))
        };
        return new(lead, word, cast, $" ({count} tracked {PublishedOfficialView.Noun(count, VoteFacts.MemberNoun(vote.Chamber))})", members);
    }

    public static string Anchor(PublishedVote vote, PublishedCitation citation) =>
        CitedText.Anchor(vote.Citations().TakeWhile(item => item != citation).Count());

    public static IReadOnlyList<CitedRange> RangesIn(PublishedVote vote, PublishedDocumentVersion source) =>
    [.. vote.Citations().Select((citation, index) => (citation, anchor: CitedText.Anchor(index)))
        .DistinctBy(item => item.citation).Where(item => item.citation.ExtractionId == source.ExtractionId)
        .Select(item => new CitedRange(item.anchor, item.citation.Start, item.citation.Length))];
}
