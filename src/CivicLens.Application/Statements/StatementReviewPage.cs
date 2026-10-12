using System.Collections.Immutable;

namespace CivicLens.Application.Statements;

public sealed record StatementReviewPage(ImmutableArray<StatementReviewListItem> Items, string? NewerCursor, string? OlderCursor);
