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
        new("Kind", KindValue(vote.Kind)),
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

    private static string KindValue(PublishedVoteKind kind) =>
        kind == PublishedVoteKind.Other
            ? $"{PublishedRecordLabels.OtherQuestion}. The question is quoted in the Result panel."
            : PublishedRecordLabels.VoteKind(kind);

    public static string Totals(PublishedVote vote)
    {
        var body = vote.Chamber == PublishedChamber.Senate ? "senators" : "House members";
        return vote.Totals is { } totals
            ? $"Casts of all {body}: Yea {totals.Yea}, Nay {totals.Nay}, Present {totals.Present}, Not voting {totals.NotVoting}"
            : $"Votes for each candidate from all {body}: {string.Join(", ", CandidateTotals(vote))}";
    }

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

    public static string FactLabel(PublishedVoteFact fact) => fact.Text[..fact.Text.IndexOf(':')];

    public static IEnumerable<PublishedVoteFact> ResultFacts(PublishedVote vote) =>
        vote.Facts.Where(fact => fact.Citation != vote.VotedOnCitation && fact.Citation != vote.KindCitation);

    public static string Anchor(PublishedVote vote, PublishedCitation citation) =>
        PublishedRecordView.CitationAnchor(vote.Citations().TakeWhile(item => item != citation).Count());

    public static IReadOnlyList<CitedRange> RangesIn(PublishedVote vote, PublishedDocumentVersion source) =>
    [.. vote.Citations().Select((citation, index) => (citation, anchor: PublishedRecordView.CitationAnchor(index)))
        .DistinctBy(item => item.citation).Where(item => item.citation.ExtractionId == source.ExtractionId)
        .Select(item => new CitedRange(item.anchor, item.citation.Start, item.citation.Length))];
}
