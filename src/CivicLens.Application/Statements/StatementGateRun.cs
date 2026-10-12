using System.Collections.Immutable;
using CivicLens.Core.Analysis;

namespace CivicLens.Application.Statements;

public sealed record StatementGateRun(AnalysisRun Run, string? IssueRevisionId, string? Reason, ImmutableArray<StatementGateQuote> Quotes);
