using System.Collections.Immutable;

namespace CivicLens.Application.Statements;

internal sealed record StatementGateOutput(ImmutableArray<StatementGateOutputIssue> TrackedIssues,
    ImmutableArray<StatementGateOutputPolicyArea> PolicyAreas, double Confidence, bool? NamesBillOrVote = null);

internal sealed record StatementGateOutputIssue(string Id, string Quote, double Confidence);

internal sealed record StatementGateOutputPolicyArea(string Id, double Confidence);
