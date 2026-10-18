using System.Collections.Immutable;
using CivicLens.Core.Actions;

namespace CivicLens.Application.Actions;

public sealed record ActionReviewPage(ImmutableArray<ActionReview> Items,
    ImmutableDictionary<string, int> Priorities, string? NewerCursor, string? OlderCursor);
