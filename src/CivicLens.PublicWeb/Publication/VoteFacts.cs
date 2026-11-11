using CivicLens.Publication.Contracts;
using CivicLens.PublicWeb.Components.Evidence;
using CivicLens.PublicWeb.Components.Ui;

namespace CivicLens.PublicWeb.Publication;

/// <summary>The one owner of the cited facts of a vote and of the nouns they use. The vote page and the live record page both call it.</summary>
public static class VoteFacts
{
    public static string MemberNoun(PublishedChamber chamber) => chamber == PublishedChamber.Senate ? "senator" : "House member";

    public static string KindName(PublishedVoteKind kind, PublishedCitation? kindCitation) =>
        HasNoQuestion(kind, kindCitation) ? PlainVoteWords.OtherKind : PlainVoteWords.Kind(kind);

    /// <summary>The facts that come from the cited lines of the source. The result, the totals, and the date show once, each with its source line link.</summary>
    public static IReadOnlyList<Fact> Cited(VoteFactsSource vote, Func<PublishedCitation, string> anchor)
    {
        var noun = MemberNoun(vote.Chamber);
        var resultFact = vote.Facts.First(fact => fact.Text == PublishedVote.ResultPrefix + vote.Result);
        var countFact = vote.Facts.FirstOrDefault(fact => fact.Text.StartsWith(PublishedVote.CountPrefix, StringComparison.Ordinal));
        var shown = new[] { resultFact, countFact };
        return
        [
            new Fact("Voted on", Moment.Day(vote.VotedOn)).CitedAt(anchor(vote.VotedOnCitation)),
            new Fact("Kind", KindValue(vote)) { TermKey = PublishedRecordLabels.VoteKindTermKey(vote.Kind) },
            .. vote.KindCitation is { } kind
                ? [new Fact("Question", PlainVoteWords.Question(vote.Kind, PublishedRecordLabels.VoteFactValue(kind.Quote))).CitedAt(anchor(kind))]
                : Array.Empty<Fact>(),
            new Fact("Result", PlainVoteWords.Result(vote.Result)).CitedAt(anchor(resultFact.Citation)),
            .. vote.Totals is { } totals
                ? [new Fact("Totals", $"Casts of all {noun}s: {PlainVoteWords.Totals(totals)}").CitedAt(countFact is null ? null : anchor(countFact.Citation))]
                : Array.Empty<Fact>(),
            .. vote.Facts.Where(fact => !shown.Contains(fact) && fact.Citation != vote.VotedOnCitation && fact.Citation != vote.KindCitation)
                .Select(fact => new Fact(PublishedRecordLabels.VoteResultLabel(fact, noun + "s"), PublishedRecordLabels.VoteFactValue(fact.Text)).CitedAt(anchor(fact.Citation)))
        ];
    }

    public static IReadOnlyList<Fact> Identity(PublishedChamber chamber, int rollCall, int congress, int session, PublishedVoteSource source) =>
    [
        new("Chamber", chamber.ToString()),
        new("Roll call", $"{rollCall}, Congress {congress}, session {session}"),
        new("Data source", PublishedRecordLabels.VoteSource(source))
    ];

    public static string SavedData(PublishedVoteSource source) => source switch
    {
        PublishedVoteSource.Voteview => "Voteview files",
        PublishedVoteSource.CongressGov => "Congress.gov API page",
        _ => throw new ArgumentOutOfRangeException(nameof(source), source, "Vote source has no saved data name.")
    };

    private static bool HasNoQuestion(PublishedVoteKind kind, PublishedCitation? kindCitation) =>
        kind == PublishedVoteKind.Other && kindCitation is null;

    private static string KindValue(VoteFactsSource vote) =>
        HasNoQuestion(vote.Kind, vote.KindCitation)
            ? $"{PlainVoteWords.OtherKind}. The source gives no question."
            : PlainVoteWords.Kind(vote.Kind);
}
