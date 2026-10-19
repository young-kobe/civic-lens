using System.Globalization;
using CivicLens.Host.Components.Evidence;
using CivicLens.Host.Components.Ui;
using CivicLens.Host.Review;
using CivicLens.Publication.Contracts;

namespace CivicLens.Host.Publication;

internal static class PublishedVoteView
{
    public static IReadOnlyList<Fact> Facts(PublishedVote vote) =>
    [
        new("Voted on", Moment.Iso(vote.VotedOn)),
        new("Kind", PublishedRecordLabels.VoteKind(vote.Kind)),
        new("Roll call", $"{vote.RollCallNumber}, Congress {vote.Congress}, session {vote.Session}"),
        Fact.List("Tracked issues", vote.TrackedIssueIds),
        new("Published", $"From the official record, checked {Moment.Full(vote.ApprovedAtUtc)}; first published {Moment.Full(vote.FirstPublishedAtUtc)}"),
        .. PublishedRecordLabels.Wording(vote.TextOrigin),
        new("Revision", vote.RevisionNumber.ToString(CultureInfo.InvariantCulture))
    ];

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
