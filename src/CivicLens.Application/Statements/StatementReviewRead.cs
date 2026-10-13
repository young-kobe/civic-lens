using CivicLens.Application.Collection;
using CivicLens.Core.Documents;
using CivicLens.Core.Statements;

namespace CivicLens.Application.Statements;

public sealed record StatementReviewRead(StatementReview Review, string SourceId, string Url, DocumentExtraction Extraction,
    string? AnalysisRunId, CollectionConfigurationRevision? ConfigurationRevision)
{
    public DateOnly FirstCapturedOn => StatementGateEvidence.CapturedOn(Extraction);
}
