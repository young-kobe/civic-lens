using CivicLens.Application.Collection.Bills;
using CivicLens.Application.Collection.Discovery;
using CivicLens.Application.Collection.HouseVotes;
using CivicLens.Application.Collection.Jobs;
using CivicLens.Application.Documents;
using CivicLens.Application.Issues;
using CivicLens.Application.Roster;
using CivicLens.Collection.Contracts;
using CivicLens.Core.Collection;
using CivicLens.Core.Documents;

namespace CivicLens.Application.Collection.Processing;

public sealed class JobPreparation(
    ICollectionJobStore jobs,
    ICollectionAttemptStore attempts,
    IDiscoveryAdmissionStore admissions,
    IBillStreamAdmissionStore billAdmissions,
    IHouseVoteAdmissionStore voteAdmissions,
    IMemberRosterStore roster,
    ICaptureBodyReader bodies,
    IEvidenceProcessingStore processing,
    IDocumentExtractionStore extractions,
    IDocumentTextExtractor textExtractor,
    IssueConfigurationRevision? issues)
{
    private const string NotModified = "notModified";
    private const string IssuesConfigurationUnavailable = "issuesConfigurationUnavailable";
    private readonly IssueConfiguration? issueConfiguration = issues?.ReadConfiguration();
    private readonly ApiResponseReader responses = new(attempts, bodies);

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
            ApiResource.HouseVoteList => AdmitHouseVoteListAsync(job, artifactRoot, cancellationToken),
            ApiResource.HouseVoteDetail => AdmitHouseVoteDetailAsync(job, artifactRoot, cancellationToken),
            ApiResource.HouseVoteMembers => AdmitHouseVoteMembersAsync(job, artifactRoot, cancellationToken),
            ApiResource.MemberRoster => AdmitMemberRosterAsync(job, artifactRoot, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(job))
        };

    private async Task<JobPreparationResult> AdmitBillListAsync(CollectionJobRecord job, string artifactRoot,
        CancellationToken cancellationToken)
    {
        var window = BillStreamWindow.TryRead(job.Definition.Url)
            ?? throw new InvalidDataException("Bill list job URL has no window.");
        var configured = ConfiguredSourceOf(job);
        var response = await responses.ReadAsync(job, artifactRoot, cancellationToken);
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
        var response = await responses.ReadAsync(job, artifactRoot, cancellationToken);
        var bill = CongressBillParser.Parse(response.Body, expected);
        var extractionId = await SaveExtractionAsync(response.Captured, CongressBillParser.Version,
            CongressBillRenderer.NormalizationVersion, CongressBillRenderer.RenderDetail(bill), cancellationToken);
        var covered = configured.CoveredBioguideIdsOn(BillGate.DayOf(bill.UpdateDate));
        var gate = BillGate.AtDetail(bill, covered, issueConfiguration);
        return PreparedFrom(await billAdmissions.AdmitDetailAsync(configured, job.JobId, bill, gate, issues.Id,
            extractionId, cancellationToken));
    }

    private async Task<JobPreparationResult> AdmitCosponsorPageAsync(CollectionJobRecord job, string artifactRoot,
        CancellationToken cancellationToken)
    {
        var configured = ConfiguredSourceOf(job);
        var bill = ReadPageBill(configured, job, ApiResource.BillCosponsors);
        var response = await responses.ReadAsync(job, artifactRoot, cancellationToken);
        var page = CongressBillCosponsorsParser.Parse(response.Body, response.Request, configured.SourceUrl, bill);
        var extractionId = await SaveExtractionAsync(response.Captured, CongressBillCosponsorsParser.Version,
            CongressBillRenderer.NormalizationVersion, CongressBillRenderer.RenderCosponsors(bill, page), cancellationToken);
        return PreparedFrom(await billAdmissions.AdmitCosponsorPageAsync(configured, job.JobId, page, extractionId, cancellationToken));
    }

    private async Task<JobPreparationResult> AdmitActionsPageAsync(CollectionJobRecord job, string artifactRoot,
        CancellationToken cancellationToken)
    {
        var configured = ConfiguredSourceOf(job);
        var bill = ReadPageBill(configured, job, ApiResource.BillActions);
        var response = await responses.ReadAsync(job, artifactRoot, cancellationToken);
        var page = CongressBillActionsParser.Parse(response.Body, response.Request, configured.SourceUrl, bill);
        var extractionId = await SaveExtractionAsync(response.Captured, CongressBillActionsParser.Version,
            CongressBillRenderer.NormalizationVersion, CongressBillRenderer.RenderActions(bill, page), cancellationToken);
        return PreparedFrom(await billAdmissions.AdmitActionsPageAsync(configured, job.JobId, page, extractionId, cancellationToken));
    }

    private async Task<JobPreparationResult> AdmitSummariesPageAsync(CollectionJobRecord job, string artifactRoot,
        CancellationToken cancellationToken)
    {
        var configured = ConfiguredSourceOf(job);
        var bill = ReadPageBill(configured, job, ApiResource.BillSummaries);
        var response = await responses.ReadAsync(job, artifactRoot, cancellationToken);
        var page = CongressBillSummariesParser.Parse(response.Body, response.Request, configured.SourceUrl, bill);
        var texts = new List<CongressBillSummaryText>();
        foreach (var summary in page.Summaries)
            texts.Add(new CongressBillSummaryText(summary, await textExtractor.ExtractHtmlFragmentAsync(summary.Html, cancellationToken)));
        var parserVersion = CongressBillSummariesParser.ExtractionVersion(textExtractor);
        var extractionId = await SaveExtractionAsync(response.Captured, parserVersion,
            CongressBillRenderer.NormalizationVersion, CongressBillRenderer.RenderSummaries(bill, texts), cancellationToken);
        return PreparedFrom(await billAdmissions.AdmitSummariesPageAsync(configured, job.JobId, page, extractionId, cancellationToken));
    }

    private async Task<JobPreparationResult> AdmitHouseVoteListAsync(CollectionJobRecord job, string artifactRoot,
        CancellationToken cancellationToken)
    {
        var configured = ConfiguredSourceOf(job);
        var response = await responses.ReadAsync(job, artifactRoot, cancellationToken);
        var page = HouseVoteListParser.Parse(response.Body, response.Request, configured.SourceUrl);
        return PreparedFrom(await voteAdmissions.AdmitListPageAsync(configured, job.JobId, page, cancellationToken));
    }

    private async Task<JobPreparationResult> AdmitHouseVoteDetailAsync(CollectionJobRecord job, string artifactRoot,
        CancellationToken cancellationToken)
    {
        var configured = ConfiguredSourceOf(job);
        var expected = HouseVoteRollUrl.ReadDetail(configured.SourceUrl, job.Definition.Url);
        var response = await responses.ReadAsync(job, artifactRoot, cancellationToken);
        var detail = HouseVoteParser.Parse(response.Body, expected);
        var extractionId = await SaveExtractionAsync(response.Captured, HouseVoteParser.Version,
            HouseVoteRenderer.NormalizationVersion, HouseVoteRenderer.RenderDetail(detail), cancellationToken);
        return PreparedFrom(await voteAdmissions.AdmitDetailAsync(configured, job.JobId, detail.Vote, extractionId, cancellationToken));
    }

    private async Task<JobPreparationResult> AdmitHouseVoteMembersAsync(CollectionJobRecord job, string artifactRoot,
        CancellationToken cancellationToken)
    {
        var configured = ConfiguredSourceOf(job);
        var expected = HouseVoteRollUrl.ReadMembers(configured.SourceUrl, job.Definition.Url);
        var response = await responses.ReadAsync(job, artifactRoot, cancellationToken);
        var members = HouseVoteMembersParser.Parse(response.Body, expected);
        var extractionId = await SaveExtractionAsync(response.Captured, HouseVoteMembersParser.Version,
            HouseVoteRenderer.NormalizationVersion, HouseVoteRenderer.RenderMembers(members), cancellationToken);
        return PreparedFrom(await voteAdmissions.AdmitMembersAsync(configured, job.JobId, members.Vote, extractionId, cancellationToken));
    }

    private async Task<JobPreparationResult> AdmitMemberRosterAsync(CollectionJobRecord job, string artifactRoot,
        CancellationToken cancellationToken)
    {
        var response = await responses.ReadAsync(job, artifactRoot, cancellationToken);
        await roster.AdmitAsync(response.Captured.AttemptId, MemberRosterParser.Parse(response.Body), cancellationToken);
        return JobPreparationResult.Prepared();
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

    private async Task<string> SaveExtractionAsync(CapturedAttemptResult captured, string parserVersion,
        string normalizationVersion, string text, CancellationToken cancellationToken) =>
        (await extractions.SaveAsync(new DocumentExtraction(captured, parserVersion, normalizationVersion, text),
            cancellationToken)).ExtractionId;

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
