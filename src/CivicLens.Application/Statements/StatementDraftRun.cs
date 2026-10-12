using CivicLens.Core.Analysis;

namespace CivicLens.Application.Statements;

public sealed record StatementDraftRun(AnalysisRun Run, string? Reason);
