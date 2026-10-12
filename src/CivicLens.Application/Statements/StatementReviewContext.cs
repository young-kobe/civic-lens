using System.Collections.Immutable;
using CivicLens.Application.Analysis;

namespace CivicLens.Application.Statements;

public sealed record StatementReviewContext(StatementGateEvidence Article, StatementAllowedSelections Allowed,
    ImmutableArray<AnalysisOfficial> Officials);
