using CivicLens.Application.Collection.Bills;
using CivicLens.Application.Collection.Discovery;
using CivicLens.Application.Collection.FederalRegister;
using CivicLens.Application.Collection.FeedStatements;
using CivicLens.Application.Collection.HouseVotes;
using CivicLens.Application.Collection.MemberPhotos;
using CivicLens.Application.Collection.VoteviewVotes;
using CivicLens.Application.Collection.Jobs;
using CivicLens.Application.Collection.Votes;
using CivicLens.Application.Documents;
using CivicLens.Application.ExecutiveActions;
using CivicLens.Application.Issues;
using CivicLens.Application.Roster;
using CivicLens.Collection.Contracts;
using CivicLens.Core.Collection;
using CivicLens.Core.Documents;
using Microsoft.Extensions.Logging;

namespace CivicLens.Application.Collection.Processing;

public sealed class JobPreparation(
    ICollectionJobStore jobs,
    ICollectionAttemptStore attempts,
    IDiscoveryAdmissionStore admissions,
    IBillStreamAdmissionStore billAdmissions,
    IVoteAdmissionStore voteAdmissions,
    IFederalRegisterAdmissionStore federalRegisterAdmissions,
    IMemberRosterStore roster,
    IExecutiveRosterStore executives,
    ICaptureBodyReader bodies,
    IEvidenceProcessingStore processing,
    IDocumentExtractionStore extractions,
    IDocumentTextExtractor textExtractor,
    IssueConfigurationRevision? issues,
    ILogger<JobPreparation> logger)
{
    private const string NotModified = "notModified";
    private const string VoteviewJobMissing = "voteviewJobMissing";
    private const string VoteviewCountMismatch = "voteviewCountMismatch";
    private const string FeedStatementTextInvalid = "feedStatementTextInvalid";
    private const string ApiResourceMissing = "apiResourceMissing";
    private const string RetiredApiResource = "retiredApiResource";
    private const string BillTextVersionsMissing = "billTextVersionsMissing";
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
        return await PrepareConfiguredAsync(job, artifactRoot, cancellationToken);
    }

    private async Task<JobPreparationResult> PrepareConfiguredAsync(CollectionJobRecord job, string artifactRoot,
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
                    if (configured.StatementTextInFeed)
                    {
                        var feedError = await SaveFeedStatementsAsync(configured, jobAttempt.Request, captured, artifactRoot,
                            cancellationToken);
                        preparationError ??= feedError;
                    }
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

    private async Task<string?> SaveFeedStatementsAsync(ConfiguredCollectionSource configured, CollectionRequest request,
        CapturedAttemptResult feed, string artifactRoot, CancellationToken cancellationToken)
    {
        var body = await bodies.ReadAsync(feed, artifactRoot, cancellationToken);
        FeedStatementReading reading;
        try
        {
            reading = await FeedStatementExtractions.ReadAsync(body, feed, request, textExtractor, cancellationToken);
        }
        catch (InvalidDataException)
        {
            return FeedStatementTextInvalid;
        }
        if (reading.UnreadableItems > 0)
            logger.LogWarning("Feed {SourceId} skipped {Skipped} items whose markup could not be read.",
                feed.SourceId, reading.UnreadableItems);
        await extractions.SaveFeedItemsAsync(reading.Items, configured.MaximumNewFeedStatements, cancellationToken);
        return null;
    }

    private Task<JobPreparationResult> PrepareApiAsync(CollectionJobRecord job, string artifactRoot,
        CancellationToken cancellationToken) => job.Definition.ApiResource switch
        {
            null => Task.FromResult(JobPreparationResult.Blocked(ApiResourceMissing)),
            ApiResource.BillList => AdmitBillListAsync(job, artifactRoot, cancellationToken),
            ApiResource.Bill => AdmitBillDetailAsync(job, artifactRoot, cancellationToken),
            ApiResource.BillCosponsors => AdmitCosponsorPageAsync(job, artifactRoot, cancellationToken),
            ApiResource.BillActions => AdmitActionsPageAsync(job, artifactRoot, cancellationToken),
            ApiResource.BillSummaries => AdmitSummariesPageAsync(job, artifactRoot, cancellationToken),
            ApiResource.BillSubjects => AdmitSubjectsPageAsync(job, artifactRoot, cancellationToken),
            ApiResource.BillTextVersions => AdmitTextVersionsAsync(job, artifactRoot, cancellationToken),
            ApiResource.BillText => AdmitBillTextAsync(job, artifactRoot, cancellationToken),
            ApiResource.HouseVoteList => AdmitHouseVoteListAsync(job, artifactRoot, cancellationToken),
            ApiResource.HouseVoteDetail => AdmitHouseVoteDetailAsync(job, artifactRoot, cancellationToken),
            ApiResource.HouseVoteMembers => AdmitHouseVoteMembersAsync(job, artifactRoot, cancellationToken),
            ApiResource.SenateVoteMenu or ApiResource.SenateVote => Task.FromResult(JobPreparationResult.Blocked(RetiredApiResource)),
            ApiResource.VoteviewRollCalls or ApiResource.VoteviewMembers => AdmitVoteviewFollowOnsAsync(job, cancellationToken),
            ApiResource.VoteviewVotes => AdmitVoteviewVotesAsync(job, artifactRoot, cancellationToken),
            ApiResource.MemberRoster => AdmitMemberRosterAsync(job, artifactRoot, cancellationToken),
            ApiResource.ExecutiveRoster => AdmitExecutiveRosterAsync(job, artifactRoot, cancellationToken),
            ApiResource.MemberPhoto => AdmitMemberPhotoAsync(job, artifactRoot, cancellationToken),
            ApiResource.FederalRegisterList => AdmitFederalRegisterListAsync(job, artifactRoot, cancellationToken),
            ApiResource.FederalRegisterDocument => AdmitFederalRegisterDocumentAsync(job, artifactRoot, cancellationToken),
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
        var bill = CongressBillParser.Parse(response.Body, expected, logger);
        var extractionId = await SaveExtractionAsync(response.Captured, CongressBillParser.ExtractionVersion,
            CongressBillRenderer.RenderDetail(bill), cancellationToken);
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
        var extractionId = await SaveExtractionAsync(response.Captured, CongressBillCosponsorsParser.ExtractionVersion,
            CongressBillRenderer.RenderCosponsors(bill, page), cancellationToken);
        return PreparedFrom(await billAdmissions.AdmitCosponsorPageAsync(configured, job.JobId, page, extractionId, cancellationToken));
    }

    private async Task<JobPreparationResult> AdmitActionsPageAsync(CollectionJobRecord job, string artifactRoot,
        CancellationToken cancellationToken)
    {
        var configured = ConfiguredSourceOf(job);
        var bill = ReadPageBill(configured, job, ApiResource.BillActions);
        var response = await responses.ReadAsync(job, artifactRoot, cancellationToken);
        var page = CongressBillActionsParser.Parse(response.Body, response.Request, configured.SourceUrl, bill);
        var extractionId = await SaveExtractionAsync(response.Captured, CongressBillActionsParser.ExtractionVersion,
            CongressBillRenderer.RenderActions(bill, page), cancellationToken);
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
        var extractionId = await SaveExtractionAsync(response.Captured, CongressBillSummariesParser.ExtractionVersion(textExtractor),
            CongressBillRenderer.RenderSummaries(bill, texts), cancellationToken);
        return PreparedFrom(await billAdmissions.AdmitSummariesPageAsync(configured, job.JobId, page, extractionId, cancellationToken));
    }

    private async Task<JobPreparationResult> AdmitSubjectsPageAsync(CollectionJobRecord job, string artifactRoot,
        CancellationToken cancellationToken)
    {
        var configured = ConfiguredSourceOf(job);
        var bill = ReadPageBill(configured, job, ApiResource.BillSubjects);
        var response = await responses.ReadAsync(job, artifactRoot, cancellationToken);
        var page = CongressBillSubjectsParser.Parse(response.Body, response.Request, configured.SourceUrl, bill);
        var extractionId = await SaveExtractionAsync(response.Captured, CongressBillSubjectsParser.ExtractionVersion,
            CongressBillRenderer.RenderSubjects(bill, page), cancellationToken);
        return PreparedFrom(await billAdmissions.AdmitSubjectsPageAsync(configured, job.JobId, page, extractionId, cancellationToken));
    }

    private async Task<JobPreparationResult> AdmitTextVersionsAsync(CollectionJobRecord job, string artifactRoot,
        CancellationToken cancellationToken)
    {
        var (_, page) = CongressBillTextVersionsJob.Parse(job, await responses.ReadAsync(job, artifactRoot, cancellationToken));
        return PreparedFrom(await billAdmissions.AdmitTextVersionsAsync(ConfiguredSourceOf(job), job.JobId, page.Newest, cancellationToken));
    }

    private async Task<JobPreparationResult> AdmitBillTextAsync(CollectionJobRecord job, string artifactRoot,
        CancellationToken cancellationToken)
    {
        var packageId = BillTextUrl.ReadPackageId(ConfiguredSourceOf(job).SourceUrl, job.Definition.Url);
        var versionsJob = await ReadParentJobAsync(ApiJobKeys.ParentJobId(job.IdempotencyKey), ApiResource.BillTextVersions, cancellationToken);
        if (versionsJob is null) return JobPreparationResult.Blocked(BillTextVersionsMissing);
        var (bill, page) = CongressBillTextVersionsJob.Parse(versionsJob, await responses.ReadAsync(versionsJob, artifactRoot, cancellationToken));
        var version = page.Versions.FirstOrDefault(candidate => candidate.PackageId == packageId)
            ?? throw new InvalidDataException("Bill text job names a package that is not in its versions page.");
        var response = await responses.ReadAsync(job, artifactRoot, cancellationToken);
        var text = await textExtractor.ExtractAsync(response.Captured, artifactRoot, null, cancellationToken);
        var reading = CongressBillRenderer.RenderText(bill, version, text);
        var extractionId = await SaveExtractionAsync(response.Captured, CongressBillTextParser.ExtractionVersion(textExtractor),
            reading.Text, cancellationToken);
        await billAdmissions.AdmitTextAsync(job.JobId,
            new CongressBillTextEvidence(version, reading, extractionId), cancellationToken);
        return JobPreparationResult.Prepared();
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
        var extractionId = await SaveExtractionAsync(response.Captured, HouseVoteParser.ExtractionVersion,
            HouseVoteRenderer.RenderDetail(detail), cancellationToken);
        return PreparedFrom(await voteAdmissions.AdmitDetailAsync(configured, job.JobId, detail.Vote, extractionId, cancellationToken));
    }

    private async Task<JobPreparationResult> AdmitHouseVoteMembersAsync(CollectionJobRecord job, string artifactRoot,
        CancellationToken cancellationToken)
    {
        var configured = ConfiguredSourceOf(job);
        var expected = HouseVoteRollUrl.ReadMembers(configured.SourceUrl, job.Definition.Url);
        var response = await responses.ReadAsync(job, artifactRoot, cancellationToken);
        var members = HouseVoteMembersParser.Parse(response.Body, expected);
        var extractionId = await SaveExtractionAsync(response.Captured, HouseVoteMembersParser.ExtractionVersion,
            HouseVoteRenderer.RenderMembers(members), cancellationToken);
        return PreparedFrom(await voteAdmissions.AdmitMembersAsync(configured, job.JobId, members.Vote, extractionId, cancellationToken));
    }

    private async Task<JobPreparationResult> AdmitFederalRegisterListAsync(CollectionJobRecord job, string artifactRoot,
        CancellationToken cancellationToken)
    {
        var configured = ConfiguredSourceOf(job);
        _ = FederalRegisterUrl.ReadList(configured.SourceUrl, job.Definition.Url);
        var response = await responses.ReadAsync(job, artifactRoot, cancellationToken);
        var page = FederalRegisterListParser.Parse(response.Body);
        return PreparedFrom(await federalRegisterAdmissions.AdmitListPageAsync(configured, page, cancellationToken));
    }

    private async Task<JobPreparationResult> AdmitFederalRegisterDocumentAsync(CollectionJobRecord job, string artifactRoot,
        CancellationToken cancellationToken)
    {
        var configured = ConfiguredSourceOf(job);
        var number = FederalRegisterUrl.ReadDocument(configured.SourceUrl, job.Definition.Url);
        var response = await responses.ReadAsync(job, artifactRoot, cancellationToken);
        var document = FederalRegisterDocumentParser.Parse(response.Body, number);
        var personId = ExecutiveActionSigner.PersonIdOf(configured.Revision.Configuration, document);
        var text = FederalRegisterRenderer.Render(document);
        var extractionId = await SaveExtractionAsync(response.Captured, FederalRegisterDocumentParser.ExtractionVersion, text, cancellationToken);
        return PreparedFrom(await federalRegisterAdmissions.AdmitDocumentAsync(configured, job.JobId,
            new FederalRegisterEvidence(document, extractionId, text, personId), cancellationToken));
    }

    private async Task<JobPreparationResult> AdmitVoteviewFollowOnsAsync(CollectionJobRecord job, CancellationToken cancellationToken)
    {
        var configured = ConfiguredSourceOf(job);
        var resource = job.Definition.ApiResource!.Value;
        var congress = VoteviewUrl.ReadCongress(configured.SourceUrl, resource, job.Definition.Url);
        return PreparedFrom(await voteAdmissions.AdmitVoteviewFollowOnsAsync(configured, resource, job.JobId, congress, cancellationToken));
    }

    private async Task<JobPreparationResult> AdmitVoteviewVotesAsync(CollectionJobRecord job, string artifactRoot,
        CancellationToken cancellationToken)
    {
        var configured = ConfiguredSourceOf(job);
        var congress = VoteviewUrl.ReadCongress(configured.SourceUrl, ApiResource.VoteviewVotes, job.Definition.Url);
        var members = await ReadParentJobAsync(ApiJobKeys.ParentJobId(job.IdempotencyKey), ApiResource.VoteviewMembers, cancellationToken);
        if (members is null) return JobPreparationResult.Blocked(VoteviewJobMissing);
        var rollCalls = await ReadParentJobAsync(ApiJobKeys.ParentJobId(members.IdempotencyKey), ApiResource.VoteviewRollCalls, cancellationToken);
        if (rollCalls is null) return JobPreparationResult.Blocked(VoteviewJobMissing);
        var chain = new VoteviewChain(congress, rollCalls.JobId, members.JobId, job.JobId);
        var rollCallsCapture = await responses.ReadCaptureAsync(rollCalls, cancellationToken);
        var membersCapture = await responses.ReadCaptureAsync(members, cancellationToken);
        var votesCapture = await responses.ReadCaptureAsync(job, cancellationToken);
        var hashes = new VoteviewBodies(rollCallsCapture.Captured.Capture.Sha256, membersCapture.Captured.Capture.Sha256,
            votesCapture.Captured.Capture.Sha256, VoteviewParser.ExtractionVersion);
        if (await voteAdmissions.HasVoteviewCandidateOfBodiesAsync(configured, congress, hashes, cancellationToken))
            return JobPreparationResult.Prepared(duplicates: 1);
        var rollCallsResponse = await responses.ReadBodyAsync(rollCallsCapture, artifactRoot, cancellationToken);
        var membersResponse = await responses.ReadBodyAsync(membersCapture, artifactRoot, cancellationToken);
        var votesResponse = await responses.ReadBodyAsync(votesCapture, artifactRoot, cancellationToken);
        var rollCallRows = VoteviewParser.ParseRollCalls(rollCallsResponse.Body, congress);
        var joined = VoteviewRoll.Join(rollCallRows, VoteviewParser.ParseCasts(votesResponse.Body, congress),
            VoteviewParser.ParseMembers(membersResponse.Body, congress));
        var rolls = joined.Select(roll => new VoteviewRollExtraction(roll.RollCall,
            new DocumentExtraction(votesResponse.Captured, VoteviewParser.ExtractionVersion, VoteviewRenderer.Render(roll), part: roll.RollCall.Part))).ToArray();
        var changed = await voteAdmissions.SelectChangedVoteviewRollsAsync(configured, congress, rolls, cancellationToken);
        foreach (var roll in changed) _ = await extractions.SaveAsync(roll.Extraction, cancellationToken);
        var prepared = PreparedFrom(await voteAdmissions.AdmitVoteviewVotesAsync(configured, chain, votesResponse.Captured.ObservedAt, changed,
            cancellationToken));
        var skipped = rollCallRows.Count - joined.Count;
        if (skipped == 0) return prepared;
        logger.LogWarning("Voteview Congress {Congress} skipped {Skipped} roll calls whose member counts differ from the roll call file.",
            congress, skipped);
        return prepared with { ErrorCode = VoteviewCountMismatch };
    }

    private async Task<CollectionJobRecord?> ReadParentJobAsync(string parentJobId, ApiResource expected, CancellationToken cancellationToken)
    {
        var parent = await jobs.GetAsync(parentJobId, cancellationToken);
        return parent is { State: CollectionJobState.Succeeded } && parent.Definition.ApiResource == expected ? parent : null;
    }

    private async Task<JobPreparationResult> AdmitMemberRosterAsync(CollectionJobRecord job, string artifactRoot,
        CancellationToken cancellationToken)
    {
        var response = await responses.ReadAsync(job, artifactRoot, cancellationToken);
        var attemptId = response.Captured.AttemptId;
        var photos = EnabledPhotoSourceOf(job);
        if (!await roster.HasSnapshotOfCaptureAsync(attemptId, cancellationToken))
            await roster.AdmitAsync(attemptId, MemberRosterParser.Parse(response.Body), photos, cancellationToken);
        else if (photos is not null)
            await roster.AdmitPhotoJobsAsync(photos, cancellationToken);
        return JobPreparationResult.Prepared();
    }

    private async Task<JobPreparationResult> AdmitExecutiveRosterAsync(CollectionJobRecord job, string artifactRoot,
        CancellationToken cancellationToken)
    {
        var response = await responses.ReadAsync(job, artifactRoot, cancellationToken);
        var attemptId = response.Captured.AttemptId;
        if (!await executives.HasSnapshotOfCaptureAsync(attemptId, cancellationToken))
            await executives.AdmitAsync(attemptId, ExecutiveRosterParser.Parse(response.Body), cancellationToken);
        return JobPreparationResult.Prepared();
    }

    private async Task<JobPreparationResult> AdmitMemberPhotoAsync(CollectionJobRecord job, string artifactRoot,
        CancellationToken cancellationToken)
    {
        var configured = ConfiguredSourceOf(job);
        var bioguideId = MemberPhotoUrl.ReadBioguideId(configured.SourceUrl, job.Definition.Url);
        var person = configured.Revision.Configuration.People.SingleOrDefault(candidate => candidate.BioguideId == bioguideId)
            ?? throw new InvalidDataException("Member photo job names a person who is not configured.");
        var response = await responses.ReadAsync(job, artifactRoot, cancellationToken);
        if (!MemberPhotoImage.IsValid(response.Captured.Response.ContentType, response.Body))
            throw new InvalidDataException("Member photo is not a JPEG of the published size.");
        await roster.AdmitPhotoAsync(new MemberPhoto(response.Captured.AttemptId, person.Id, job.Definition.Url,
            MemberPhoto.MemberGuideCredit), cancellationToken);
        return JobPreparationResult.Prepared();
    }

    private static CongressBillListItem ReadPageBill(ConfiguredCollectionSource configured, CollectionJobRecord job,
        ApiResource resource) =>
        BillPageUrl.TryRead(configured.SourceUrl, resource, job.Definition.Url)?.Bill
        ?? throw new InvalidDataException("Bill page job URL is not a page of its bill resource.");

    private static ConfiguredCollectionSource? EnabledPhotoSourceOf(CollectionJobRecord job) =>
        job.Definition.ConfigurationRevision!.Configuration.SourceOf(ApiParser.MemberPhotos) is { Enabled: true } source
            ? new ConfiguredCollectionSource(job.Definition.ConfigurationRevision, source.Id, job.Definition.CoverageAsOf)
            : null;

    private static ConfiguredCollectionSource ConfiguredSourceOf(CollectionJobRecord job) =>
        new(job.Definition.ConfigurationRevision!, job.Definition.SourceId, job.Definition.CoverageAsOf);

    private static JobPreparationResult PreparedFrom(DiscoveryAdmissionResult result) =>
        JobPreparationResult.Prepared(admitted: result.AdmittedCount, deferred: result.DeferredCount,
            duplicates: result.DuplicateCount);

    private async Task<string> SaveExtractionAsync(CapturedAttemptResult captured, string extractionVersion,
        string text, CancellationToken cancellationToken) =>
        (await extractions.SaveAsync(new DocumentExtraction(captured, extractionVersion, text),
            cancellationToken)).ExtractionId;

}
