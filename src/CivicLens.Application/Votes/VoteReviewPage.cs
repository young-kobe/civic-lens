using System.Collections.Immutable;
using CivicLens.Core.Votes;

namespace CivicLens.Application.Votes;

public sealed record VoteReviewPage(ImmutableArray<VoteReview> Items, string? NewerCursor, string? OlderCursor);
