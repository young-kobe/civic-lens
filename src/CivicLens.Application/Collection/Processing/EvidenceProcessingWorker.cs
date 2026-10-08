using CivicLens.Application.Collection.Discovery;
using CivicLens.Application.Collection.Jobs;
using CivicLens.Application.Documents;
using CivicLens.Collection.Contracts;
using CivicLens.Core.Collection;
using CivicLens.Core.Documents;

namespace CivicLens.Application.Collection.Processing;

/// <summary>Prepares successful collections and advances bounded captured-page evidence work.</summary>
public sealed class EvidenceProcessingWorker(
    ICollectionJobStore jobs,
    ICollectionAttemptStore attempts,
    IDiscoveryAdmissionStore admissions,
    IEvidenceProcessingStore processing,
    ExtractDocument extract,
    GetDocumentHistory history,
    CompareDocuments compare)
{
    private EvidenceProcessingCursor? preparationCursor;
    public const int MaximumBatchSize = 100;
    private const int MaximumAttempts = 5;

    public async Task<int> ExecuteAsync(string artifactRoot, int batchSize, TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactRoot);
        if (batchSize is < 1 or > MaximumBatchSize) throw new ArgumentOutOfRangeException(nameof(batchSize));
        var failures = 0;
        var preparation = await processing.GetPreparationJobIdsAsync(preparationCursor, batchSize, cancellationToken);
        preparationCursor = preparation.NextCursor;
        foreach (var jobId in preparation.JobIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var job = await jobs.GetAsync(jobId, cancellationToken);
                if (job is { State: CollectionJobState.Succeeded })
                    await processing.EnsurePreparationAsync(jobId, job.Definition.SourceId, job.Definition.Url, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception exception) when (IsRecoverable(exception)) { failures++; }
        }

        var eligible = await processing.GetEligibleAsync(batchSize, cancellationToken);
        foreach (var record in eligible)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try { await ProcessAsync(record, artifactRoot, leaseDuration, cancellationToken); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception exception) when (IsRecoverable(exception)) { failures++; }
        }
        return failures;
    }

    private async Task PrepareAsync(EvidenceProcessingClaim claim, CancellationToken cancellationToken)
    {
        var jobId = claim.Record.JobId;
        var job = await jobs.GetAsync(jobId, cancellationToken);
        if (job is null || job.State != CollectionJobState.Succeeded) return;
        var definition = job.Definition;
        if (definition.ConfigurationRevision is null || definition.CoverageAsOf is null)
        {
            foreach (var jobAttempt in job.Attempts.Where(item => item.Resolution?.Outcome == CollectionJobAttemptOutcome.Succeeded))
            {
                var stored = await attempts.GetAsync(jobAttempt.AttemptId, cancellationToken);
                if (stored?.AttemptResult is CapturedAttemptResult captured)
                    _ = await processing.EnsureAsync(jobId, captured.AttemptId, captured.SourceId,
                        captured.RequestedUrl, cancellationToken);
            }
            await CompleteAsync(claim, EvidenceProcessingStage.Complete, EvidenceProcessingStatus.Blocked,
                EvidenceProcessingOutcome.Blocked, "configurationUnavailable", cancellationToken);
            return;
        }

        var configuration = definition.ConfigurationRevision.ReadConfiguration();
        var source = configuration.Sources.Single(candidate => candidate.Id == definition.SourceId);
        var configured = new ConfiguredCollectionSource(configuration, source.Id, definition.CoverageAsOf);
        var admitted = 0;
        var deferred = 0;
        var duplicates = 0;
        string? preparationError = null;
        foreach (var jobAttempt in job.Attempts.Where(item => item.Resolution?.Outcome == CollectionJobAttemptOutcome.Succeeded))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var stored = await attempts.GetAsync(jobAttempt.AttemptId, cancellationToken);
            if (stored?.AttemptResult is not CapturedAttemptResult captured)
            {
                if (stored?.AttemptResult is NotModifiedAttemptResult)
                    preparationError = "notModified";
                continue;
            }
            if (definition.Mode is CollectionMode.Feed or CollectionMode.Html)
            {
                if (stored.Discovery?.Status == DiscoveryStatus.Parsed)
                {
                    var key = $"automatic-check/{jobId}/{jobAttempt.AttemptId}";
                    var result = await admissions.AdmitAutomaticCheckAsync(configured, jobAttempt.AttemptId, key, cancellationToken);
                    admitted += result.AdmittedCount;
                    deferred += result.DeferredCount;
                    duplicates += result.DuplicateCount;
                }
                else preparationError = stored.Discovery is null ? "notModified" : "discoveryUnavailable";
            }
            if (definition.Mode == CollectionMode.Page)
                _ = await processing.EnsureAsync(jobId, captured.AttemptId, captured.SourceId,
                    captured.RequestedUrl, cancellationToken);
        }
        var isNotModified = preparationError == "notModified";
        await CompleteAsync(claim, EvidenceProcessingStage.Complete,
            preparationError is null || isNotModified ? EvidenceProcessingStatus.Succeeded : EvidenceProcessingStatus.Blocked,
            preparationError is null || isNotModified ? EvidenceProcessingOutcome.Prepared : EvidenceProcessingOutcome.Blocked,
            preparationError, cancellationToken, admittedCount: admitted, deferredCount: deferred,
            duplicateCount: duplicates);
    }

    private async Task ProcessAsync(EvidenceProcessingRecord record, string artifactRoot,
        TimeSpan leaseDuration, CancellationToken cancellationToken)
    {
        var claim = await processing.TryClaimAsync(record.JobId, record.AttemptId, leaseDuration, cancellationToken);
        if (claim is null) return;
        using var ownership = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var heartbeat = RenewLeaseAsync(claim, leaseDuration, ownership);
        try
        {
            await AdvanceAsync(claim, artifactRoot, ownership.Token);
        }
        catch (OperationCanceledException) when (ownership.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            // Ownership was fenced by another worker. The stale worker cannot checkpoint.
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            using var release = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try { _ = await processing.ReleaseAsync(claim, release.Token); } catch { }
            throw;
        }
        catch (Exception exception) when (IsRecoverable(exception))
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
                return;
            }
            var retry = claim.Record.Attempts < MaximumAttempts;
            var status = retry ? EvidenceProcessingStatus.RetryWaiting : EvidenceProcessingStatus.Failed;
            var delay = TimeSpan.FromSeconds(Math.Min(300, Math.Pow(2, claim.Record.Attempts)));
            var update = new EvidenceProcessingCheckpoint(record.JobId, record.AttemptId, claim.Record.Stage,
                EvidenceProcessingStatus.Running, claim.LeaseToken, claim.Fence, claim.Record.Stage, status,
                Outcome: retry ? null : EvidenceProcessingOutcome.Failed,
                ErrorCode: SafeErrorCode(exception), RetryAt: retry ? DateTimeOffset.UtcNow.Add(delay) : null);
            using var retryTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            _ = await processing.CheckpointAsync(update, retryTimeout.Token);
        }
        finally
        {
            ownership.Cancel();
            try { await heartbeat; } catch (OperationCanceledException) { }
        }
    }

    private async Task AdvanceAsync(EvidenceProcessingClaim claim, string artifactRoot, CancellationToken cancellationToken)
    {
        var record = claim.Record;
        if (record.Stage == EvidenceProcessingStage.Preparation)
        {
            await PrepareAsync(claim, cancellationToken);
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
        var configuration = job.Definition.ConfigurationRevision.ReadConfiguration();
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
        var validatedHistory = await history.ExecuteAsync(record.SourceId, record.RequestedUrl,
            GetDocumentHistory.MaximumObservations, cancellationToken);
        var observations = validatedHistory.Observations;
        var currentIndex = -1;
        for (var index = 0; index < observations.Length; index++)
            if (observations[index].Attempt.AttemptResult.AttemptId == record.AttemptId) currentIndex = index;
        if (currentIndex < 0) throw new InvalidDataException("Captured page is missing from retained document history.");
        var current = observations[currentIndex].Extractions.SingleOrDefault(item => item.ExtractionId == record.ExtractionId)
            ?? throw new InvalidDataException("Processing extraction is missing from retained history.");
        if (currentIndex == 0)
        {
            await CompleteAsync(claim, EvidenceProcessingStage.Complete, EvidenceProcessingStatus.Succeeded,
                EvidenceProcessingOutcome.Baseline, null, cancellationToken);
            return;
        }

        var previous = observations[currentIndex - 1];
        var previousCapture = previous.Attempt.PriorCapturedAttempt ??
            previous.Attempt.AttemptResult as CapturedAttemptResult;
        var priorProcessing = previousCapture is null ? null :
            await processing.GetByAttemptIdAsync(previousCapture.AttemptId, cancellationToken);
        if (priorProcessing?.Status is EvidenceProcessingStatus.Pending or EvidenceProcessingStatus.Running or
            EvidenceProcessingStatus.RetryWaiting || priorProcessing is null && previousCapture is not null &&
            await processing.IsAwaitingPreparationAsync(previousCapture.AttemptId, cancellationToken))
        {
            if (!await processing.DeferAsync(claim, DateTimeOffset.UtcNow.AddSeconds(2), cancellationToken))
                throw new OperationCanceledException("Processing lease was lost.");
            return;
        }
        var previousExtraction = previousCapture is null ? null : previous.Extractions.FirstOrDefault(item =>
            item.SourceAttempt.AttemptId == previousCapture.AttemptId &&
            item.ParserVersion == current.ParserVersion && item.NormalizationVersion == current.NormalizationVersion &&
            ((item.Profile is null && current.Profile is null) || item.Profile?.Matches(current.Profile) == true));
        if (previousExtraction is null)
        {
            await CompleteAsync(claim, EvidenceProcessingStage.Complete, EvidenceProcessingStatus.Succeeded,
                EvidenceProcessingOutcome.Baseline,
                previousCapture is null ? "historyGap" : "incompatibleHistory", cancellationToken);
            return;
        }
        var comparison = await compare.CompareAsync(previousExtraction, current, cancellationToken);
        if (comparison.Status != DocumentComparisonStatus.Complete)
        {
            await CompleteAsync(claim, EvidenceProcessingStage.Complete, EvidenceProcessingStatus.Blocked,
                EvidenceProcessingOutcome.Blocked, comparison.Status == DocumentComparisonStatus.LimitExceeded
                    ? "comparisonLimitExceeded" : "incompatibleHistory", cancellationToken);
            return;
        }
        var saved = await compare.SaveAsync(comparison, cancellationToken);
        await CompleteAsync(claim, EvidenceProcessingStage.Complete, EvidenceProcessingStatus.Succeeded,
            comparison.Hunks.IsEmpty ? EvidenceProcessingOutcome.Unchanged : EvidenceProcessingOutcome.Changed,
            null, cancellationToken, saved.ComparisonId);
    }

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

    private async Task RenewLeaseAsync(EvidenceProcessingClaim claim, TimeSpan leaseDuration,
        CancellationTokenSource ownership)
    {
        var interval = TimeSpan.FromTicks(Math.Max(TimeSpan.FromSeconds(1).Ticks, leaseDuration.Ticks / 3));
        while (!ownership.IsCancellationRequested)
        {
            try { await Task.Delay(interval, ownership.Token); }
            catch (OperationCanceledException) when (ownership.IsCancellationRequested) { return; }
            bool renewed;
            try { renewed = await processing.RenewAsync(claim, leaseDuration, ownership.Token); }
            catch (OperationCanceledException) when (ownership.IsCancellationRequested) { return; }
            catch (Exception exception) when (IsRecoverable(exception))
            {
                ownership.Cancel();
                return;
            }
            if (!renewed)
            {
                ownership.Cancel();
                return;
            }
        }
    }

    private static string SafeErrorCode(Exception exception) => exception is DocumentHistoryLimitException
        ? "historyLimitExceeded" : exception.GetType().Name.Length <= 128 ? exception.GetType().Name : "processingFailure";

    private static bool IsPermanent(Exception exception) => exception is ArgumentException or InvalidDataException or
        DocumentHistoryLimitException;

    private static bool IsRecoverable(Exception exception) =>
        exception is not OutOfMemoryException and not StackOverflowException;
}
