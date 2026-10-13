using CivicLens.Application.Collection.Bills;
using CivicLens.Application.Collection.Discovery;
using CivicLens.Application.Collection.Jobs;
using CivicLens.Application.Documents;
using CivicLens.Application.Issues;
using CivicLens.Collection.Contracts;
using CivicLens.Core.Collection;
using CivicLens.Core.Documents;

namespace CivicLens.Application.Collection.Processing;

public sealed class JobPreparation(
    ICollectionJobStore jobs,
    ICollectionAttemptStore attempts,
    IDiscoveryAdmissionStore admissions,
    IBillStreamAdmissionStore billAdmissions,
    ICaptureBodyReader bodies,
    IEvidenceProcessingStore processing,
    IDocumentExtractionStore extractions,
    IDocumentTextExtractor textExtractor,
    IssueConfigurationRevision? issues)
{
    private const string NotModified = "notModified";
    private const string IssuesConfigurationUnavailable = "issuesConfigurationUnavailable";
    private readonly IssueConfiguration? issueConfiguration = issues?.ReadConfiguration();

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

    private Task<JobPreparationResult> PrepareApiAsync(CollectionJobRecord job, string artifactRoot,
        CancellationToken cancellationToken) => job.Definition.ApiResource switch
        {
            null => Task.FromResult(JobPreparationResult.Prepared()),
            ApiResource.BillList => AdmitBillListAsync(job, artifactRoot, cancellationToken),
            ApiResource.Bill => AdmitBillDetailAsync(job, artifactRoot, cancellationToken),
            ApiResource.BillCosponsors => AdmitCosponsorPageAsync(job, artifactRoot, cancellationToken),
            ApiResource.BillActions => AdmitActionsPageAsync(job, artifactRoot, cancellationToken),
            ApiResource.BillSummaries => AdmitSummariesPageAsync(job, artifactRoot, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(job))
        };

    private async Task<JobPreparationResult> AdmitBillListAsync(CollectionJobRecord job, string artifactRoot,
        CancellationToken cancellationToken)
    {
        var window = BillStreamWindow.TryRead(job.Definition.Url)
            ?? throw new InvalidDataException("Bill list job URL has no window.");
        var configured = ConfiguredSourceOf(job);
        var response = await ReadApiResponseAsync(job, artifactRoot, cancellationToken);
        var page = CongressBillListParser.Parse(response.Body, response.Request, window);
        return PreparedFrom(await billAdmissions.AdmitListPageAsync(configured, job.JobId, window, page, cancellationToken));
    }

    private async Task<JobPreparationResult> AdmitBillDetailAsync(CollectionJobRecord job, string artifactRoot,
        CancellationToken cancellationToken)
    {
        if (issues is null || issueConfiguration is null) return JobPreparationResult.Blocked(IssuesConfigurationUnavailable);
        var configured = ConfiguredSourceOf(job);
        var expected = CongressBillListItem.TryReadDetailUrl(configured.SourceUrl, job.Definition.Url)
            ?? throw new InvalidDataException("Bill detail job URL is not a bill detail URL.");
        var response = await ReadApiResponseAsync(job, artifactRoot, cancellationToken);
        var bill = CongressBillParser.Parse(response.Body, expected);
        await SaveExtractionAsync(response.Captured, CongressBillParser.Version, CongressBillRenderer.RenderDetail(bill), cancellationToken);
        var covered = configured.CoveredBioguideIdsOn(BillGate.DayOf(bill.UpdateDate));
        var gate = BillGate.AtDetail(bill, covered, issueConfiguration);
        return PreparedFrom(await billAdmissions.AdmitDetailAsync(configured, job.JobId, bill, gate, issues.Id,
            cancellationToken));
    }

    private async Task<JobPreparationResult> AdmitCosponsorPageAsync(CollectionJobRecord job, string artifactRoot,
        CancellationToken cancellationToken)
    {
        var configured = ConfiguredSourceOf(job);
        var bill = ReadPageBill(configured, job, ApiResource.BillCosponsors);
        var response = await ReadApiResponseAsync(job, artifactRoot, cancellationToken);
        var page = CongressBillCosponsorsParser.Parse(response.Body, response.Request, configured.SourceUrl, bill);
        await SaveExtractionAsync(response.Captured, CongressBillCosponsorsParser.Version, CongressBillRenderer.RenderCosponsors(bill, page),
            cancellationToken);
        return PreparedFrom(await billAdmissions.AdmitCosponsorPageAsync(configured, job.JobId, page, cancellationToken));
    }

    private async Task<JobPreparationResult> AdmitActionsPageAsync(CollectionJobRecord job, string artifactRoot,
        CancellationToken cancellationToken)
    {
        var configured = ConfiguredSourceOf(job);
        var bill = ReadPageBill(configured, job, ApiResource.BillActions);
        var response = await ReadApiResponseAsync(job, artifactRoot, cancellationToken);
        var page = CongressBillActionsParser.Parse(response.Body, response.Request, configured.SourceUrl, bill);
        await SaveExtractionAsync(response.Captured, CongressBillActionsParser.Version, CongressBillRenderer.RenderActions(bill, page),
            cancellationToken);
        return PreparedFrom(await billAdmissions.AdmitActionsPageAsync(configured, job.JobId, page, cancellationToken));
    }

    private async Task<JobPreparationResult> AdmitSummariesPageAsync(CollectionJobRecord job, string artifactRoot,
        CancellationToken cancellationToken)
    {
        var configured = ConfiguredSourceOf(job);
        var bill = ReadPageBill(configured, job, ApiResource.BillSummaries);
        var response = await ReadApiResponseAsync(job, artifactRoot, cancellationToken);
        var page = CongressBillSummariesParser.Parse(response.Body, response.Request, configured.SourceUrl, bill);
        var texts = new List<CongressBillSummaryText>();
        foreach (var summary in page.Summaries)
            texts.Add(new CongressBillSummaryText(summary, await textExtractor.ExtractHtmlFragmentAsync(summary.Html, cancellationToken)));
        var parserVersion = CongressBillSummariesParser.ExtractionVersion(textExtractor);
        await SaveExtractionAsync(response.Captured, parserVersion, CongressBillRenderer.RenderSummaries(bill, texts),
            cancellationToken);
        return PreparedFrom(await billAdmissions.AdmitSummariesPageAsync(configured, job.JobId, page, cancellationToken));
    }

    private static CongressBillListItem ReadPageBill(ConfiguredCollectionSource configured, CollectionJobRecord job,
        ApiResource resource) =>
        BillPageUrl.TryRead(configured.SourceUrl, resource, job.Definition.Url)?.Bill
        ?? throw new InvalidDataException("Bill page job URL is not a page of its bill resource.");

    private static ConfiguredCollectionSource ConfiguredSourceOf(CollectionJobRecord job) =>
        new(job.Definition.ConfigurationRevision!, job.Definition.SourceId, job.Definition.CoverageAsOf);

    private static JobPreparationResult PreparedFrom(DiscoveryAdmissionResult result) =>
        JobPreparationResult.Prepared(admitted: result.AdmittedCount, deferred: result.DeferredCount,
            duplicates: result.DuplicateCount);

    private async Task SaveExtractionAsync(CapturedAttemptResult captured, string parserVersion, string text,
        CancellationToken cancellationToken) =>
        _ = await extractions.SaveAsync(new DocumentExtraction(captured, parserVersion, CongressBillRenderer.NormalizationVersion, text),
            cancellationToken);

    private async Task<ApiResponse> ReadApiResponseAsync(CollectionJobRecord job,
        string artifactRoot, CancellationToken cancellationToken)
    {
        var jobAttempt = job.Attempts.Single(item => item.Resolution?.Outcome == CollectionJobAttemptOutcome.Succeeded);
        var stored = await attempts.GetAsync(jobAttempt.AttemptId, cancellationToken);
        if (stored?.AttemptResult is not CapturedAttemptResult captured)
            throw new InvalidDataException("API attempt has no captured response.");
        return new ApiResponse(jobAttempt.Request, captured, await bodies.ReadAsync(captured, artifactRoot, cancellationToken));
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

    private sealed record ApiResponse(CollectionRequest Request, CapturedAttemptResult Captured, byte[] Body);
}
