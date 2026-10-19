using System.Collections.Immutable;
using CivicLens.Core.Votes;

namespace CivicLens.Application.Votes;

public sealed record VoteReviewPage(ImmutableArray<VoteReview> Items,
    ImmutableDictionary<string, int> Priorities, string? NewerCursor, string? OlderCursor);
