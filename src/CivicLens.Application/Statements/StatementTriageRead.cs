using System.Collections.Immutable;

namespace CivicLens.Application.Statements;

public sealed record StatementTriageRead(ImmutableArray<StatementTriageEvidence> Items, string? NewerCursor, string? OlderCursor);
