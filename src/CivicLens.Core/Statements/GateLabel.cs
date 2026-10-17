using System.Collections.Immutable;

namespace CivicLens.Core.Statements;

public sealed record GateLabel(ImmutableArray<string> IssueIds, bool NamesBillOrVote);
