using System.Collections.Immutable;
using CivicLens.Core.Votes;

namespace CivicLens.Application.Votes;

public static class VoteFacts
{
    public static VoteFact? Find(ImmutableArray<VoteFact> facts, string prefix) =>
        facts.FirstOrDefault(fact => fact.Text.StartsWith(prefix, StringComparison.Ordinal));

    /// <summary>The text after the prefix of the first fact with that prefix, or null when no fact has it.</summary>
    public static string? ValueOf(ImmutableArray<VoteFact> facts, string prefix) =>
        Find(facts, prefix)?.Text[prefix.Length..];

    public static string? QuestionOf(ImmutableArray<VoteFact> facts) => ValueOf(facts, VoteFactPrefixes.Question);

    public static string ResultOf(string recordId, ImmutableArray<VoteFact> facts) =>
        ValueOf(facts, VoteFactPrefixes.Result) ?? throw new ArgumentException($"Vote {recordId} has no result line.", nameof(facts));
}
