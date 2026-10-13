using CivicLens.Core;

namespace CivicLens.Application.Collection.Processing;

public static class EvidenceProcessingLifecycle
{
    public static bool CanTransition(EvidenceProcessingRecord record, EvidenceProcessingCheckpoint update)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(update);
        if (!MatchesClaim(record, update) || !HasValidOutcome(record.Stage, update) || !HasValidEvidence(record, update))
            return false;
        return (record.Stage, update.Stage, update.Status) switch
        {
            (EvidenceProcessingStage.Preparation, EvidenceProcessingStage.Complete, EvidenceProcessingStatus.Succeeded) => true,
            (EvidenceProcessingStage.Preparation, EvidenceProcessingStage.Complete, EvidenceProcessingStatus.Blocked) => true,
            (EvidenceProcessingStage.Extraction, EvidenceProcessingStage.Comparison, EvidenceProcessingStatus.Pending) => true,
            (EvidenceProcessingStage.Extraction, EvidenceProcessingStage.Complete, EvidenceProcessingStatus.Blocked) => true,
            (EvidenceProcessingStage.Comparison, EvidenceProcessingStage.Complete, EvidenceProcessingStatus.Succeeded) => true,
            (EvidenceProcessingStage.Comparison, EvidenceProcessingStage.Complete, EvidenceProcessingStatus.Blocked) => true,
            (_, _, EvidenceProcessingStatus.RetryWaiting) => update.Stage == record.Stage && update.RetryDelay is not null,
            (_, _, EvidenceProcessingStatus.Failed) => update.Stage == record.Stage,
            _ => false
        };
    }

    public static int AttemptsAfter(EvidenceProcessingRecord record, EvidenceProcessingCheckpoint update)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(update);
        return update.Stage == update.ExpectedStage ? record.Attempts : 0;
    }

    public static long? RetryAtTicks(EvidenceProcessingCheckpoint update, long databaseNowTicks)
    {
        ArgumentNullException.ThrowIfNull(update);
        return update.Status == EvidenceProcessingStatus.RetryWaiting
            ? databaseNowTicks + (update.RetryDelay?.Ticks ?? 0)
            : null;
    }

    public static int AttemptsAfterWaiting(EvidenceProcessingRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return Math.Max(record.Attempts - 1, 0);
    }

    private static bool MatchesClaim(EvidenceProcessingRecord record, EvidenceProcessingCheckpoint update) =>
        record.Status == EvidenceProcessingStatus.Running && !string.IsNullOrWhiteSpace(record.LeaseToken) &&
        record.Fence > 0 && record.JobId == update.JobId && record.AttemptId == update.AttemptId &&
        record.Stage == update.ExpectedStage && record.Status == update.ExpectedStatus &&
        record.LeaseToken == update.LeaseToken && record.Fence == update.Fence;

    private static bool HasValidOutcome(EvidenceProcessingStage stage, EvidenceProcessingCheckpoint update)
    {
        if (update.ErrorCode is not null && (update.ErrorCode.Length > 128 || string.IsNullOrWhiteSpace(update.ErrorCode)))
            return false;
        if (update.Status != EvidenceProcessingStatus.RetryWaiting && update.RetryDelay is not null)
            return false;
        return update.Status switch
        {
            EvidenceProcessingStatus.Pending => update.Outcome is null && update.ErrorCode is null,
            EvidenceProcessingStatus.RetryWaiting => update.Outcome is null && update.ErrorCode is not null &&
                update.RetryDelay is { } delay && delay >= TimeSpan.Zero && delay <= EvidenceProcessingPolicy.MaximumRetryDelay,
            EvidenceProcessingStatus.Blocked => update.Outcome == EvidenceProcessingOutcome.Blocked && update.ErrorCode is not null,
            EvidenceProcessingStatus.Failed => update.Outcome == EvidenceProcessingOutcome.Failed && update.ErrorCode is not null,
            EvidenceProcessingStatus.Succeeded when stage == EvidenceProcessingStage.Preparation => update.Outcome == EvidenceProcessingOutcome.Prepared,
            EvidenceProcessingStatus.Succeeded when stage == EvidenceProcessingStage.Comparison =>
                update.Outcome is EvidenceProcessingOutcome.Baseline or EvidenceProcessingOutcome.Unchanged or EvidenceProcessingOutcome.Changed,
            _ => false
        };
    }

    private static bool HasValidEvidence(EvidenceProcessingRecord record, EvidenceProcessingCheckpoint update)
    {
        if (update.ExtractionId is not null && !LowercaseHex.Is(update.ExtractionId, 64)) return false;
        if (update.ComparisonId is not null && !LowercaseHex.Is(update.ComparisonId, 64)) return false;
        if (record.ExtractionId is not null && update.ExtractionId is not null && record.ExtractionId != update.ExtractionId)
            return false;
        var needsExtraction = update.Stage == EvidenceProcessingStage.Comparison ||
            update.Outcome is EvidenceProcessingOutcome.Baseline or EvidenceProcessingOutcome.Unchanged or EvidenceProcessingOutcome.Changed;
        if (needsExtraction && !LowercaseHex.Is(update.ExtractionId ?? record.ExtractionId, 64)) return false;
        return update.Outcome switch
        {
            EvidenceProcessingOutcome.Changed or EvidenceProcessingOutcome.Unchanged => LowercaseHex.Is(update.ComparisonId, 64),
            EvidenceProcessingOutcome.Baseline or EvidenceProcessingOutcome.Prepared => update.ComparisonId is null,
            _ => update.ComparisonId is null
        };
    }

}
