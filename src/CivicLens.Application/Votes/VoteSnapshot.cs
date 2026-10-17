using System.Collections.Immutable;
using CivicLens.Core.Votes;

namespace CivicLens.Application.Votes;

public sealed record VoteSnapshot(ImmutableArray<VoteFact> Facts, ImmutableArray<MemberVote> Members, string Fingerprint, string? PolicyArea, DateOnly Day,
    ImmutableDictionary<string, string> OfficialNames, ImmutableArray<string> PersonNames);
