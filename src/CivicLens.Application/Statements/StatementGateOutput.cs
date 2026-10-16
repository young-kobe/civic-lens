using System.Collections.Immutable;

namespace CivicLens.Application.Statements;

internal sealed record StatementGateOutput(ImmutableArray<StatementGateOutputIssue> TrackedIssues,
    ImmutableArray<StatementGateOutputPolicyArea> PolicyAreas, bool NamesBillOrVote, double Confidence);

internal sealed record StatementGateOutputIssue(string Id, string Quote, double Confidence);

internal sealed record StatementGateOutputPolicyArea(string Id, double Confidence);
