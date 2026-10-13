using System.Collections.Immutable;
using CivicLens.Application.Analysis;
using CivicLens.Core.Documents;
using CivicLens.Core.Statements;

namespace CivicLens.Application.Statements;

public sealed record StatementReviewDetail(StatementReview Review, string SourceId, string Url, DocumentExtraction Extraction,
    string? AnalysisRunId, StatementAllowedSelections Allowed, ImmutableArray<AnalysisOfficial> Officials);
