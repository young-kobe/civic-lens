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
    public static IReadOnlyList<Fact> Facts(PublishedVote vote, PublicationCatalog catalog) =>
    [
        new Fact("Voted on", Moment.Day(vote.VotedOn)).CitedAt(Anchor(vote, vote.VotedOnCitation)),
        new Fact("Kind", KindValue(vote)) { TermKey = PublishedRecordLabels.VoteKindTermKey(vote.Kind) },
        .. Question(vote),
        new Fact("Result", PlainVoteWords.Result(vote.Result)).CitedAt(Anchor(vote, ResultFact(vote).Citation)),
        .. Totals(vote),
        .. ResultFacts(vote).Select(fact => new Fact(PublishedRecordLabels.VoteResultLabel(fact, MemberNoun(vote) + "s"), PublishedRecordLabels.VoteFactValue(fact.Text)).CitedAt(Anchor(vote, fact.Citation))),
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

    public static (string Yes, string No)? Meaning(PublishedVote vote) => VoteMeaning.Of(vote.Kind);

    public static string? NextStepOf(PublishedVote vote) => NextStep.Of(vote.Kind, vote.Result, vote.Chamber, vote.BillType);

    private static string KindValue(PublishedVote vote) =>
        vote.Kind == PublishedVoteKind.Other && vote.KindCitation is null
            ? $"{PlainVoteWords.OtherKind}. The source gives no question."
            : PlainVoteWords.Kind(vote.Kind);

    private static IEnumerable<Fact> Question(PublishedVote vote) =>
        vote.KindCitation is { } kind ? [new Fact("Question", PlainVoteWords.Question(vote.Kind, PublishedRecordLabels.VoteFactValue(kind.Quote))).CitedAt(Anchor(vote, kind))] : [];

    private static IEnumerable<Fact> Totals(PublishedVote vote) =>
        vote.Totals is { } totals
            ? [new Fact("Totals", $"Casts of all {MemberNoun(vote)}s: {PlainVoteWords.Totals(totals)}")
                .CitedAt(CountFact(vote) is { } count ? Anchor(vote, count.Citation) : null)]
            : [];

    private static string MemberNoun(PublishedVote vote) => vote.Chamber == PublishedChamber.Senate ? "senator" : "House member";

    public static string Caveat(PublishedVote vote) =>
        "This record lists only the officials that Civic Lens tracks. " + vote.Source switch
        {
            PublishedVoteSource.Voteview => "Voteview stores each vote as a number code. Civic Lens shows that code as a word such as Yes (Yea) or No (Nay). The Senate roll call may use other words. Voteview does not list a Vice President tie-break vote.",
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

    private static PublishedVoteFact ResultFact(PublishedVote vote) =>
        vote.Facts.First(fact => fact.Text == PublishedVote.ResultPrefix + vote.Result);

    private static PublishedVoteFact? CountFact(PublishedVote vote) =>
        vote.Facts.FirstOrDefault(fact => fact.Text.StartsWith(PublishedVote.CountPrefix, StringComparison.Ordinal));

    private static IEnumerable<PublishedVoteFact> ResultFacts(PublishedVote vote)
    {
        var shownElsewhere = new[] { ResultFact(vote), CountFact(vote) };
        return vote.Facts.Where(fact => !shownElsewhere.Contains(fact) && fact.Citation != vote.VotedOnCitation && fact.Citation != vote.KindCitation);
    }

    public static IEnumerable<CastGroup> CastGroups(PublishedVote vote) =>
        vote.Members.GroupBy(member => member.Cast)
            .OrderBy(group => CastRank(group.Key))
            .ThenByDescending(group => PublishedOfficialVote.IsCandidate(group.Key) ? group.Count() : 0)
            .ThenBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => Heading(vote, group.Key, group.Count(), ByName(group)));

    private static PublishedMemberVote[] ByName(IEnumerable<PublishedMemberVote> members) =>
        [.. members.OrderBy(member => member.Official.Name, StringComparer.OrdinalIgnoreCase).ThenBy(member => member.Official.Id, StringComparer.Ordinal)];

    public const string SplitVoteText = "Split vote";

    public const string PartiesAgreedText = "Both parties agreed";

    public static (string Text, string Meaning)? ListBadge(PublishedRecordEntry entry) => entry.Alignment switch
    {
        PublishedVoteAlignment.Split => (SplitVoteText, PublishedOfficialView.SplitVoteMeaning),
        PublishedVoteAlignment.PartiesAgree => (PartiesAgreedText, PublishedOfficialView.PartiesAgreedMeaning),
        _ => null
    };

    public static string? ListYeaNay(PublishedRecordEntry entry) =>
        entry.YeaCount is { } yea && entry.NayCount is { } nay ? PlainVoteWords.YesNo(yea, nay) : null;

    private const string CountsWho = "of all members of the chamber who voted Yes or No";

    public static string? ListCounts(PublishedRecordEntry entry) =>
        ListYeaNay(entry) is { } counts ? $"Casts {CountsWho}: {counts}." : null;

    public static IEnumerable<string> ListNotes(IEnumerable<PublishedRecordEntry> entries, bool defineBadges, ReleaseBillLines billLines)
    {
        var votes = entries.Where(entry => entry.Kind == PublishedRecordKind.Vote).ToArray();
        if (defineBadges && votes.Any(entry => entry.Alignment == PublishedVoteAlignment.Split)) yield return PublishedOfficialView.SplitVoteMeaning;
        if (defineBadges && votes.Any(entry => entry.Alignment == PublishedVoteAlignment.PartiesAgree)) yield return PublishedOfficialView.PartiesAgreedMeaning;
        if (votes.Any(entry => ListYeaNay(entry) is not null)) yield return $"Yes and No counts are casts {CountsWho}.";
        if (billLines.AnyOf(entries)) yield return PublishedRecordLabels.BillLineSource;
    }

    public static VoteBadge? Badge(PublishedVote vote)
    {
        var democrats = $"most {PartyOf(vote, PublishedPartyMajority.Democrat)}";
        var republicans = $"most {PartyOf(vote, PublishedPartyMajority.Republican)}";
        const string cast = "who cast Yes or No";
        if (vote.IsSplit())
            return new(SplitVoteText, $"{SplitVoteText}: {democrats} {cast} voted {SideWord(vote, PublishedPartyMajority.Democrat)}, and {republicans} {cast} voted {SideWord(vote, PublishedPartyMajority.Republican)}.", PartyCounts(vote));
        return vote.PartiesAgree() ? new(PartiesAgreedText, $"{PartiesAgreedText}: {democrats} and {republicans} {cast} voted {SideWord(vote, PublishedPartyMajority.Democrat)}.", PartyCounts(vote)) : null;
    }

    private static string SideWord(PublishedVote vote, string party) =>
        PlainVoteWords.CastWord(vote.MajorityOf(party) ?? "") ?? throw new InvalidOperationException("A vote with a badge has a majority for each party.");

    private static string PartyCounts(PublishedVote vote) =>
        string.Join(' ', new[] { PublishedPartyMajority.Democrat, PublishedPartyMajority.Republican }.Select(party => PartyCount(vote, party))) +
        " These counts include every member of the party, tracked or not. An independent counts with the party they caucus with, so these counts can differ from the source totals. Yes here means Yea or Aye in the record. No means Nay.";

    private static string PartyCount(PublishedVote vote, string party)
    {
        var count = vote.PartyMajorities.Single(majority => majority.Party == party);
        return $"{PartyOf(vote, party)}: {count.YeaCount} Yes, {count.NayCount} No.";
    }

    public const string BreakMeaning = "A member is listed when they cast Yes or No against the cast of most members of their own party on this vote. A current independent counts with the party they caucus with. A former independent counts with no party. A member with another party, or with no known party, is not listed. Present and Not voting never count as a break.";

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
        return new(lead, word, cast, $" ({count} tracked {PublishedOfficialView.Noun(count, MemberNoun(vote))})", members);
    }

    public static string Anchor(PublishedVote vote, PublishedCitation citation) =>
        PublishedRecordView.CitationAnchor(vote.Citations().TakeWhile(item => item != citation).Count());

    public static IReadOnlyList<CitedRange> RangesIn(PublishedVote vote, PublishedDocumentVersion source) =>
    [.. vote.Citations().Select((citation, index) => (citation, anchor: PublishedRecordView.CitationAnchor(index)))
        .DistinctBy(item => item.citation).Where(item => item.citation.ExtractionId == source.ExtractionId)
        .Select(item => new CitedRange(item.anchor, item.citation.Start, item.citation.Length))];
}
