using System.Collections.Immutable;

namespace CivicLens.Application.Statements;

public sealed record StatementReviewPage(ImmutableArray<StatementReviewListItem> Items,
    ImmutableDictionary<string, int> Priorities, string? NewerCursor, string? OlderCursor);
