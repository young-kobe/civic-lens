namespace CivicLens.Application.Collection.Processing;

public sealed record JobPreparationResult(EvidenceProcessingStatus Status, EvidenceProcessingOutcome Outcome,
    string? ErrorCode = null, int AdmittedCount = 0, int DeferredCount = 0, int DuplicateCount = 0)
{
    public static JobPreparationResult Prepared(string? errorCode = null, int admitted = 0, int deferred = 0,
        int duplicates = 0) =>
        new(EvidenceProcessingStatus.Succeeded, EvidenceProcessingOutcome.Prepared, errorCode, admitted, deferred, duplicates);

    public static JobPreparationResult Blocked(string errorCode, int admitted = 0, int deferred = 0, int duplicates = 0) =>
        new(EvidenceProcessingStatus.Blocked, EvidenceProcessingOutcome.Blocked, errorCode, admitted, deferred, duplicates);
}
