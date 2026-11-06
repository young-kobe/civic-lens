using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using CivicLens.Application.Publication;
using CivicLens.Core.Votes;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Votes;

/// <summary>Content votes decide what the bill does, so the model reads the whole summary. Procedure votes decide a step, so the model reads the bill line only.</summary>
public enum VoteDecidesMode { Content, Procedure }

/// <summary>
/// The one owner of what the model may read to say what a vote decides, and of the words that code checks the answer against. A vote has this input
/// only when it concerns a bill that has a Congressional Research Service summary with a bill line. The question and result come from
/// <see cref="PlainVoteWords"/>, so the model never writes them. The source hash names everything the model reads except the question and result,
/// which the fact lines already cover.
/// </summary>
public sealed record VoteDecidesInput(VoteDecidesMode Mode, string BillLine, string SummaryText, string? BillTitle, string BillNumber, string Question,
    string Result, string? ResultWord, ImmutableHashSet<string> CountNumbers, string SourceHash)
{
    /// <summary>The text that the model reads, so the only text that a figure of the answer may come from.</summary>
    public string ReadText => string.Join('\n', new[] { Mode == VoteDecidesMode.Content ? SummaryText : BillLine, BillTitle }.OfType<string>());

    public static VoteDecidesInput? Of(VoteKind kind, ImmutableArray<VoteFact> facts, int? billNumber, VoteBillSummary? summary)
    {
        if (summary is null || billNumber is null || VoteFacts.ValueOf(facts, VoteFactPrefixes.Result) is not { } result) return null;
        var mode = IsContent(kind) ? VoteDecidesMode.Content : VoteDecidesMode.Procedure;
        var published = PublishedVoteKinds.Of(kind);
        var question = VoteFacts.QuestionOf(facts) is { } source ? PlainVoteWords.Question(published, source) : PlainVoteWords.Kind(published);
        return new(mode, summary.Line, summary.Text, VoteFacts.ValueOf(facts, VoteFactPrefixes.Title), billNumber.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
            question, PlainVoteWords.Result(result), PlainVoteWords.ResultWord(result), CountNumbersOf(facts),
            HashOf(mode, summary, VoteFacts.ValueOf(facts, VoteFactPrefixes.Title)));
    }

    private static bool IsContent(VoteKind kind) =>
        kind is VoteKind.Passage or VoteKind.Suspension or VoteKind.ConferenceReport or VoteKind.VetoOverride;

    private static ImmutableHashSet<string> CountNumbersOf(ImmutableArray<VoteFact> facts) =>
        [.. facts.Where(fact => VoteFactPrefixes.CountPrefixes.Any(prefix => fact.Text.StartsWith(prefix, StringComparison.Ordinal)))
            .SelectMany(fact => VoteDecidesCheck.Numbers(fact.Text))];

    private static string HashOf(VoteDecidesMode mode, VoteBillSummary summary, string? title) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', "vote-decides-source-v1", mode, summary.Line,
            mode == VoteDecidesMode.Content ? summary.Text : "", title))));
}
