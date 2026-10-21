using CivicLens.Application.Collection.Jobs;
using CivicLens.Application.Documents;
using CivicLens.Collection.Contracts;
using CivicLens.Core.Collection;
using CivicLens.Core.Documents;

namespace CivicLens.Application.Collection.Processing;

/// <summary>Prepares successful collections and advances bounded captured-page evidence work.</summary>
public sealed class EvidenceProcessingWorker(
    ICollectionJobStore jobs,
    JobPreparation preparation,
    IEvidenceProcessingStore processing,
    ExtractDocument extract,
    GetDocumentHistory history,
    CompareDocuments compare,
    int concurrency)
{
    public const int MaximumBatchSize = 100;
    private const int MaximumAttempts = 5;

    private readonly int degree = EvidenceProcessingPolicy.ValidateConcurrency(concurrency);

    public async Task<EvidenceProcessingWorkerResult> ExecuteAsync(string artifactRoot, int batchSize, TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactRoot);
        if (batchSize is < 1 or > MaximumBatchSize) throw new ArgumentOutOfRangeException(nameof(batchSize));
        EvidenceProcessingPolicy.ValidateLeaseDuration(leaseDuration);
        var failures = 0;
        var progress = 0;
        var eligible = await processing.GetEligibleAsync(batchSize, cancellationToken);
        await Parallel.ForEachAsync(eligible, new ParallelOptions
        {
            MaxDegreeOfParallelism = degree,
            CancellationToken = cancellationToken
        }, async (record, token) =>
        {
            try
            {
                if (await ProcessAsync(record, artifactRoot, leaseDuration, token)) Interlocked.Increment(ref progress);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception exception) when (WorkerLoop.IsRecoverable(exception)) { Interlocked.Increment(ref failures); }
        });
        return new(progress, failures);
    }

    private async Task PrepareAsync(EvidenceProcessingClaim claim, string artifactRoot, CancellationToken cancellationToken)
    {
        var result = await preparation.PrepareAsync(claim.Record.JobId, artifactRoot, cancellationToken);
        if (result is null) return;
        await CompleteAsync(claim, EvidenceProcessingStage.Complete, result.Status, result.Outcome, result.ErrorCode,
            cancellationToken, admittedCount: result.AdmittedCount, deferredCount: result.DeferredCount,
            duplicateCount: result.DuplicateCount);
    }

    private async Task<bool> ProcessAsync(EvidenceProcessingRecord record, string artifactRoot,
        TimeSpan leaseDuration, CancellationToken cancellationToken)
    {
        var claim = await processing.TryClaimAsync(record.JobId, record.AttemptId, leaseDuration, cancellationToken);
        if (claim is null) return false;
        using var ownership = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var heartbeat = WorkerLoop.RenewLeaseAsync(token => processing.RenewAsync(claim, leaseDuration, token), leaseDuration, ownership);
        try
        {
            await AdvanceAsync(claim, artifactRoot, ownership.Token);
        }
        catch (OperationCanceledException) when (ownership.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            // Ownership was fenced by another worker. The stale worker cannot checkpoint.
            return false;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            using var release = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try { _ = await processing.ReleaseAsync(claim, release.Token); } catch (Exception releaseFailure) when (WorkerLoop.IsRecoverable(releaseFailure)) { }
            throw;
        }
        catch (Exception exception) when (WorkerLoop.IsRecoverable(exception))
        {
            if (IsPermanent(exception))
            {
                var checkpoint = new EvidenceProcessingCheckpoint(record.JobId, record.AttemptId,
                    claim.Record.Stage, EvidenceProcessingStatus.Running, claim.LeaseToken, claim.Fence,
                    EvidenceProcessingStage.Complete, EvidenceProcessingStatus.Blocked,
                    ExtractionId: claim.Record.ExtractionId, Outcome: EvidenceProcessingOutcome.Blocked,
                    ErrorCode: SafeErrorCode(exception));
                using var recovery = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                _ = await processing.CheckpointAsync(checkpoint, recovery.Token);
                return true;
            }
            var retry = claim.Record.Attempts < MaximumAttempts;
            var status = retry ? EvidenceProcessingStatus.RetryWaiting : EvidenceProcessingStatus.Failed;
            var delay = TimeSpan.FromSeconds(Math.Min(EvidenceProcessingPolicy.MaximumRetryDelay.TotalSeconds, Math.Pow(2, claim.Record.Attempts)));
            var update = new EvidenceProcessingCheckpoint(record.JobId, record.AttemptId, claim.Record.Stage,
                EvidenceProcessingStatus.Running, claim.LeaseToken, claim.Fence, claim.Record.Stage, status,
                Outcome: retry ? null : EvidenceProcessingOutcome.Failed,
                ErrorCode: SafeErrorCode(exception), RetryDelay: retry ? delay : null);
            using var retryTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            _ = await processing.CheckpointAsync(update, retryTimeout.Token);
        }
        finally
        {
            ownership.Cancel();
            try { await heartbeat; } catch (OperationCanceledException) { }
        }
        return true;
    }

    private async Task AdvanceAsync(EvidenceProcessingClaim claim, string artifactRoot, CancellationToken cancellationToken)
    {
        var record = claim.Record;
        if (record.Stage == EvidenceProcessingStage.Preparation)
        {
            await PrepareAsync(claim, artifactRoot, cancellationToken);
            return;
        }
        var job = await jobs.GetAsync(record.JobId, cancellationToken)
            ?? throw new InvalidOperationException("Processing job disappeared.");
        if (job.Definition.ConfigurationRevision is null)
        {
            await CompleteAsync(claim, EvidenceProcessingStage.Complete, EvidenceProcessingStatus.Blocked,
                EvidenceProcessingOutcome.Blocked, "configurationUnavailable", cancellationToken);
            return;
        }
        var configuration = job.Definition.ConfigurationRevision.Configuration;
        var source = configuration.Sources.SingleOrDefault(candidate => candidate.Id == record.SourceId);
        if (source?.DocumentProfileId is null || configuration.DocumentProfiles is null)
        {
            await CompleteAsync(claim, EvidenceProcessingStage.Complete, EvidenceProcessingStatus.Blocked,
                EvidenceProcessingOutcome.Blocked, "contentProfileUnavailable", cancellationToken);
            return;
        }
        var profileConfiguration = configuration.DocumentProfiles.SingleOrDefault(profile => profile.Id == source.DocumentProfileId);
        if (profileConfiguration is null)
        {
            await CompleteAsync(claim, EvidenceProcessingStage.Complete, EvidenceProcessingStatus.Blocked,
                EvidenceProcessingOutcome.Blocked, "contentProfileUnavailable", cancellationToken);
            return;
        }

        if (record.Stage == EvidenceProcessingStage.Extraction)
        {
            var extraction = await extract.ExecuteAsync(record.AttemptId, artifactRoot,
                profileConfiguration.ToProfile(), cancellationToken);
            var checkpoint = new EvidenceProcessingCheckpoint(record.JobId, record.AttemptId, record.Stage,
                EvidenceProcessingStatus.Running, claim.LeaseToken, claim.Fence, EvidenceProcessingStage.Comparison,
                EvidenceProcessingStatus.Pending, ExtractionId: extraction.ExtractionId);
            if (!await processing.CheckpointAsync(checkpoint, cancellationToken)) throw new OperationCanceledException("Processing lease was lost.");
            return;
        }

        if (record.Stage != EvidenceProcessingStage.Comparison || record.ExtractionId is null)
            throw new InvalidDataException("Processing checkpoint has an invalid stage or missing extraction.");
        var snapshot = await ReadComparisonHistoryAsync(record, cancellationToken);
        var predecessorAttemptId = snapshot.PreviousCapture?.AttemptId;
        if (predecessorAttemptId is not null &&
            !await processing.IsPredecessorSettledAsync(predecessorAttemptId, cancellationToken))
        {
            if (!await processing.WaitForPredecessorAsync(claim, predecessorAttemptId, cancellationToken))
                throw new OperationCanceledException("Processing lease was lost.");
            return;
        }
        // Readiness and history are separate transactions. Reload after readiness so a just-committed
        // predecessor extraction cannot be missed by the snapshot read above.
        snapshot = await ReadComparisonHistoryAsync(record, cancellationToken);
        if (snapshot.PreviousCapture?.AttemptId != predecessorAttemptId)
        {
            if (!await processing.ReleaseAsync(claim, cancellationToken))
                throw new OperationCanceledException("Processing lease was lost.");
            return;
        }
        if (snapshot.Previous is null)
        {
            await CompleteAsync(claim, EvidenceProcessingStage.Complete, EvidenceProcessingStatus.Succeeded,
                EvidenceProcessingOutcome.Baseline, null, cancellationToken);
            return;
        }
        var previousExtraction = snapshot.PreviousCapture is null ? null : snapshot.Previous.Extractions.FirstOrDefault(item =>
            item.SourceAttempt.AttemptId == snapshot.PreviousCapture.AttemptId &&
            item.ParserVersion == snapshot.Current.ParserVersion &&
            item.NormalizationVersion == snapshot.Current.NormalizationVersion &&
            ((item.Profile is null && snapshot.Current.Profile is null) ||
             item.Profile?.Matches(snapshot.Current.Profile) == true));
        if (previousExtraction is null)
        {
            await CompleteAsync(claim, EvidenceProcessingStage.Complete, EvidenceProcessingStatus.Succeeded,
                EvidenceProcessingOutcome.Baseline,
                snapshot.PreviousCapture is null ? "historyGap" : "incompatibleHistory", cancellationToken);
            return;
        }
        var comparison = await compare.ExecuteAsync(previousExtraction.ExtractionId, snapshot.Current.ExtractionId,
            cancellationToken);
        if (comparison.Status != DocumentComparisonStatus.Complete)
        {
            await CompleteAsync(claim, EvidenceProcessingStage.Complete, EvidenceProcessingStatus.Blocked,
                EvidenceProcessingOutcome.Blocked, comparison.Status == DocumentComparisonStatus.LimitExceeded
                    ? "comparisonLimitExceeded" : "incompatibleHistory", cancellationToken);
            return;
        }
        await CompleteAsync(claim, EvidenceProcessingStage.Complete, EvidenceProcessingStatus.Succeeded,
            comparison.Hunks.IsEmpty ? EvidenceProcessingOutcome.Unchanged : EvidenceProcessingOutcome.Changed,
            null, cancellationToken, comparison.ComparisonId);
    }

    private async Task<ComparisonHistorySnapshot> ReadComparisonHistoryAsync(EvidenceProcessingRecord record,
        CancellationToken cancellationToken)
    {
        var documentHistory = await history.ExecuteAsync(record.SourceId, record.RequestedUrl,
            GetDocumentHistory.MaximumObservations, cancellationToken);
        var currentIndex = -1;
        for (var index = 0; index < documentHistory.Observations.Length; index++)
            if (documentHistory.Observations[index].Attempt.AttemptResult.AttemptId == record.AttemptId)
                currentIndex = index;
        if (currentIndex < 0) throw new InvalidDataException("Captured page is missing from retained document history.");
        var currentObservation = documentHistory.Observations[currentIndex];
        var current = currentObservation.Extractions.SingleOrDefault(item => item.ExtractionId == record.ExtractionId)
            ?? throw new InvalidDataException("Processing extraction is missing from retained history.");
        var previous = currentIndex == 0 ? null : documentHistory.Observations[currentIndex - 1];
        var previousCapture = previous?.Attempt.PriorCapturedAttempt ??
            previous?.Attempt.AttemptResult as CapturedAttemptResult;
        return new(current, previous, previousCapture);
    }

    private sealed record ComparisonHistorySnapshot(Core.Documents.DocumentExtraction Current,
        DocumentHistoryObservation? Previous, CapturedAttemptResult? PreviousCapture);

    private async Task CompleteAsync(EvidenceProcessingClaim claim, EvidenceProcessingStage stage,
        EvidenceProcessingStatus status, EvidenceProcessingOutcome outcome, string? errorCode,
        CancellationToken cancellationToken, string? comparisonId = null, int admittedCount = 0,
        int deferredCount = 0, int duplicateCount = 0)
    {
        var checkpoint = new EvidenceProcessingCheckpoint(claim.Record.JobId, claim.Record.AttemptId,
            claim.Record.Stage, EvidenceProcessingStatus.Running, claim.LeaseToken, claim.Fence, stage, status,
            ExtractionId: claim.Record.ExtractionId, ComparisonId: comparisonId, Outcome: outcome, ErrorCode: errorCode,
            AdmittedCount: admittedCount, DeferredCount: deferredCount, DuplicateCount: duplicateCount);
        if (!await processing.CheckpointAsync(checkpoint, cancellationToken))
            throw new OperationCanceledException("Processing lease was lost.");
    }

    private static string SafeErrorCode(Exception exception) => exception is DocumentHistoryLimitException
        ? "historyLimitExceeded" : WorkerLoop.ErrorCode(exception);

    private static bool IsPermanent(Exception exception) => exception is ArgumentException or InvalidDataException or
        DocumentHistoryLimitException;
}
