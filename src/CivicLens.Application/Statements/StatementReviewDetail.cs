using CivicLens.Core.Documents;
using CivicLens.Core.Statements;

namespace CivicLens.Application.Statements;

public sealed record StatementReviewDetail(StatementReview Review, string SourceId, string Url, DocumentExtraction Extraction,
    string? AnalysisRunId);
