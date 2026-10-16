using System.Collections.Immutable;
using CivicLens.Core.Votes;

namespace CivicLens.Application.Votes;

public sealed record VoteSnapshot(ImmutableArray<VoteFact> Facts, ImmutableArray<MemberVote> Members, string Fingerprint,
    ImmutableDictionary<string, string> OfficialNames, ImmutableArray<string> ExcludedNames);
