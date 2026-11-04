using System.Collections.Immutable;
using CivicLens.Core.Votes;

namespace CivicLens.Application.Votes;

public static class VoteFacts
{
    public static VoteFact? Find(ImmutableArray<VoteFact> facts, string prefix) =>
        facts.FirstOrDefault(fact => fact.Text.StartsWith(prefix, StringComparison.Ordinal));
}
