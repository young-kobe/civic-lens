using System.Collections.Immutable;

namespace CivicLens.Application.Statements;

public sealed record StatementTriagePage(ImmutableArray<StatementTriageRow> Rows, string? NewerCursor, string? OlderCursor);
