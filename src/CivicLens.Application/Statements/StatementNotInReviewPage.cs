using System.Collections.Immutable;

namespace CivicLens.Application.Statements;

public sealed record StatementNotInReviewPage(ImmutableArray<StatementNotInReviewItem> Items, string? NewerCursor, string? OlderCursor);
