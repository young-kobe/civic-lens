using CivicLens.Core.Documents;

namespace CivicLens.Application.Statements;

public sealed record StatementArticle(string CandidateId, DocumentExtraction Extraction)
{
    public DateOnly FirstCapturedOn => CapturedOn(Extraction);

    public static DateOnly CapturedOn(DocumentExtraction extraction) => DateOnly.FromDateTime(extraction.SourceAttempt.ObservedAt.UtcDateTime);
}
