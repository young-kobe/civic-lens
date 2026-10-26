using System.Collections.Immutable;
using CivicLens.Core.Votes;

namespace CivicLens.Application.Votes;

public sealed record VotesNeedingAction(ImmutableArray<VoteReview> Items, int Total);
