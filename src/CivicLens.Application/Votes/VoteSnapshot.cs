using System.Collections.Immutable;
using CivicLens.Core.Documents;
using CivicLens.Core.Votes;

namespace CivicLens.Application.Votes;

public sealed record VoteSnapshot(Chamber Chamber, VoteSource Source, ImmutableArray<VoteFact> Facts, SourceSpan VotedOnCitation, VoteKind Kind, SourceSpan? KindCitation, ImmutableArray<MemberVote> Members, string Fingerprint, string? PolicyArea, ImmutableArray<string> Subjects, DateOnly Day,
    ImmutableDictionary<string, string> OfficialNames, ImmutableArray<string> PersonNames);
