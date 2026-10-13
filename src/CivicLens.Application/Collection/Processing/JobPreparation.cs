using CivicLens.Application.Collection.Bills;
using CivicLens.Application.Collection.Discovery;
using CivicLens.Application.Collection.Jobs;
using CivicLens.Collection.Contracts;
using CivicLens.Core.Collection;

namespace CivicLens.Application.Collection.Processing;

public sealed class JobPreparation(
    ICollectionJobStore jobs,
    ICollectionAttemptStore attempts,
    IDiscoveryAdmissionStore admissions,
    IBillStreamAdmissionStore billAdmissions,
    ICaptureBodyReader bodies,
    IEvidenceProcessingStore processing)
{
    private const string NotModified = "notModified";

    public async Task<JobPreparationResult?> PrepareAsync(string jobId, string artifactRoot,
        CancellationToken cancellationToken)
    {
        var job = await jobs.GetAsync(jobId, cancellationToken);
        if (job is null || job.State != CollectionJobState.Succeeded) return null;
        if (job.Definition.Mode == CollectionMode.Api)
            return await PrepareApiAsync(job, artifactRoot, cancellationToken);
        if (job.Definition.ConfigurationRevision is null || job.Definition.CoverageAsOf is null)
            return await BlockWithoutConfigurationAsync(job, cancellationToken);
        return await PrepareConfiguredAsync(job, cancellationToken);
    }

    private async Task<JobPreparationResult> BlockWithoutConfigurationAsync(CollectionJobRecord job,
        CancellationToken cancellationToken)
    {
        foreach (var captured in await ReadCapturedAttemptsAsync(job, cancellationToken))
            _ = await processing.EnsureAsync(job.JobId, captured.AttemptId, captured.SourceId,
                captured.RequestedUrl, cancellationToken);
        return JobPreparationResult.Blocked("configurationUnavailable");
    }

    private async Task<JobPreparationResult> PrepareConfiguredAsync(CollectionJobRecord job,
        CancellationToken cancellationToken)
    {
        var definition = job.Definition;
        var configured = new ConfiguredCollectionSource(definition.ConfigurationRevision!, definition.SourceId, definition.CoverageAsOf);
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
                    preparationError = NotModified;
                continue;
            }
            if (definition.Mode is CollectionMode.Feed or CollectionMode.Html)
            {
                if (stored.Discovery?.Status == DiscoveryStatus.Parsed)
                {
                    var key = $"automatic-check/{job.JobId}/{jobAttempt.AttemptId}";
                    var result = await admissions.AdmitAutomaticCheckAsync(configured, jobAttempt.AttemptId, key, cancellationToken);
                    admitted += result.AdmittedCount;
                    deferred += result.DeferredCount;
                    duplicates += result.DuplicateCount;
                }
                else preparationError = stored.Discovery is null ? NotModified : "discoveryUnavailable";
            }
            if (definition.Mode == CollectionMode.Page)
                _ = await processing.EnsureAsync(job.JobId, captured.AttemptId, captured.SourceId,
                    captured.RequestedUrl, cancellationToken);
        }
        if (preparationError is null or NotModified)
            return JobPreparationResult.Prepared(preparationError, admitted, deferred, duplicates);
        return JobPreparationResult.Blocked(preparationError, admitted, deferred, duplicates);
    }

    private async Task<JobPreparationResult> PrepareApiAsync(CollectionJobRecord job, string artifactRoot,
        CancellationToken cancellationToken)
    {
        switch (job.Definition.ApiResource)
        {
            case null:
            case ApiResource.Bill:
                return JobPreparationResult.Prepared();
            case ApiResource.BillList:
                var result = await AdmitBillListAsync(job, artifactRoot, cancellationToken);
                return JobPreparationResult.Prepared(admitted: result.AdmittedCount, deferred: result.DeferredCount,
                    duplicates: result.DuplicateCount);
            default:
                throw new ArgumentOutOfRangeException(nameof(job));
        }
    }

    private async Task<DiscoveryAdmissionResult> AdmitBillListAsync(CollectionJobRecord job, string artifactRoot,
        CancellationToken cancellationToken)
    {
        var window = BillStreamWindow.TryRead(job.Definition.Url)
            ?? throw new InvalidDataException("Bill list job URL has no window.");
        var jobAttempt = job.Attempts.Single(item => item.Resolution?.Outcome == CollectionJobAttemptOutcome.Succeeded);
        var stored = await attempts.GetAsync(jobAttempt.AttemptId, cancellationToken);
        if (stored?.AttemptResult is not CapturedAttemptResult captured)
            throw new InvalidDataException("Bill list attempt has no captured response.");
        var body = await bodies.ReadAsync(captured, artifactRoot, cancellationToken);
        var page = CongressBillListParser.Parse(body, jobAttempt.Request, window);
        var configured = new ConfiguredCollectionSource(job.Definition.ConfigurationRevision!, job.Definition.SourceId,
            job.Definition.CoverageAsOf);
        return await billAdmissions.AdmitListPageAsync(configured, job.JobId, window, page, cancellationToken);
    }

    private async Task<IReadOnlyList<CapturedAttemptResult>> ReadCapturedAttemptsAsync(CollectionJobRecord job,
        CancellationToken cancellationToken)
    {
        var captured = new List<CapturedAttemptResult>();
        foreach (var jobAttempt in job.Attempts.Where(item => item.Resolution?.Outcome == CollectionJobAttemptOutcome.Succeeded))
        {
            var stored = await attempts.GetAsync(jobAttempt.AttemptId, cancellationToken);
            if (stored?.AttemptResult is CapturedAttemptResult result) captured.Add(result);
        }
        return captured;
    }
}
