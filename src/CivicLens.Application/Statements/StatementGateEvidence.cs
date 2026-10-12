using CivicLens.Core.Documents;

namespace CivicLens.Application.Statements;

public sealed record StatementGateEvidence(string CandidateId, DocumentExtraction Extraction)
{
    public DateOnly FirstCapturedOn => DateOnly.FromDateTime(Extraction.SourceAttempt.ObservedAt.UtcDateTime);
}
