using CivicLens.Application.Collection.Jobs;
using CivicLens.Application.Collection.Processing;
using CivicLens.Application.Collection;
using CivicLens.Application.Documents;
using CivicLens.Application.Review;
using CivicLens.Collection.Contracts;
using CivicLens.Core.Documents;
using CivicLens.Core.Review;

namespace CivicLens.Application.Collection.Bills;

public sealed class BackfillBillActions(IBillActionsBackfillStore store, ICollectionJobStore jobs,
    IDocumentExtractionStore extractions, ApiResponseReader responses, string artifactRoot)
{
    private const int MaximumPagesPerBill = 100;
    private const int BatchSize = 25;

    public async Task<BillActionsBackfillResult> ExecuteAsync(ReviewActor actor, int limit,
        CancellationToken cancellationToken = default)
    {
        ReviewAuthorization.RequireOwner(actor);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        int examined = 0, recorded = 0, failed = 0;
        string? after = null;
        while (recorded < limit)
        {
            var detailJobIds = await store.ListBillsWithUnverifiedActionsAsync(after,
                Math.Min(BatchSize, limit - recorded), cancellationToken);
            if (detailJobIds.Count == 0) break;
            foreach (var detailJobId in detailJobIds)
            {
                examined++;
                try
                {
                    if (await RecoverAsync(detailJobId, cancellationToken)) recorded++;
                }
                catch (Exception exception) when (exception is InvalidDataException or IOException)
                {
                    failed++;
                }
                after = detailJobId;
            }
        }
        return new(examined, recorded, failed);
    }

    private async Task<bool> RecoverAsync(string detailJobId, CancellationToken cancellationToken)
    {
        var pages = await store.ListActionPagesAsync(detailJobId, cancellationToken);
        if (pages.Count is 0 or > MaximumPagesPerBill) throw new InvalidDataException("The actions chain has an invalid page count.");
        var parsed = new List<VerifiedBillActionsPage>(pages.Count);
        var firstJob = await jobs.GetAsync(pages[0].JobId, cancellationToken)
            ?? throw new InvalidDataException("An actions page job is missing.");
        var source = new ConfiguredCollectionSource(firstJob.Definition.ConfigurationRevision!, firstJob.Definition.SourceId,
            firstJob.Definition.CoverageAsOf);
        var expected = BillPageUrl.TryRead(source.SourceUrl, ApiResource.BillActions, firstJob.Definition.Url)?.Bill
            ?? throw new InvalidDataException("An actions page URL is invalid.");
        if (expected.Congress != pages[0].Congress || expected.Type != pages[0].BillType ||
            expected.Number != pages[0].BillNumber || source.SourceId != pages[0].SourceId)
            throw new InvalidDataException("The first actions page does not match its bill gate.");
        var previousNext = (string?)null;

        for (var index = 0; index < pages.Count; index++)
        {
            var pageRef = pages[index];
            if (pageRef.PageOrder != index) throw new InvalidDataException("The actions page order has a gap.");
            var job = index == 0 ? firstJob : await jobs.GetAsync(pageRef.JobId, cancellationToken)
                ?? throw new InvalidDataException("An actions page job is missing.");
            var pageUrl = BillPageUrl.TryRead(source.SourceUrl, ApiResource.BillActions, job.Definition.Url);
            if (job.Definition.SourceId != source.SourceId || job.Definition.ApiResource != ApiResource.BillActions ||
                pageUrl is null || !pageUrl.Value.Bill.IsSameBillAs(expected) || pageUrl.Value.Offset != index * ApiPageQuery.PageLimit ||
                pageRef.Congress != expected.Congress || pageRef.BillType != expected.Type || pageRef.BillNumber != expected.Number ||
                pageRef.SourceId != source.SourceId ||
                previousNext is not null && previousNext != job.Definition.Url)
                throw new InvalidDataException("The saved actions pages do not form one ordered bill chain.");

            DocumentExtraction? extraction;
            try
            {
                extraction = await extractions.GetAsync(pageRef.ExtractionId, cancellationToken);
            }
            catch (InvalidOperationException exception)
            {
                throw new InvalidDataException("An actions page extraction is corrupt.", exception);
            }
            if (extraction is null) throw new InvalidDataException("An actions page extraction is missing.");
            if (extraction.ExtractionId != pageRef.ExtractionId ||
                extraction.ExtractionVersion != CongressBillActionsParser.ExtractionVersion || extraction.Part is not null ||
                extraction.Profile is not null)
                throw new InvalidDataException("An actions page extraction has an unexpected identity.");
            var sourceAttempt = extraction.SourceAttempt;
            var attempt = job.Attempts.SingleOrDefault(item => item.AttemptId == sourceAttempt.AttemptId &&
                item.Resolution?.Outcome == CollectionJobAttemptOutcome.Succeeded)
                ?? throw new InvalidDataException("The extraction is not pinned to a successful attempt of its page job.");
            if (attempt.Request.Url != job.Definition.Url || attempt.Request.SourceId != job.Definition.SourceId ||
                sourceAttempt.SourceId != attempt.Request.SourceId || sourceAttempt.RequestedUrl != attempt.Request.Url)
                throw new InvalidDataException("The pinned attempt request does not match its saved actions page job.");
            ApiResponse response;
            try
            {
                response = await responses.ReadBodyAsync(new ApiCapture(attempt.Request, sourceAttempt), artifactRoot, cancellationToken);
            }
            catch (NotSupportedException exception)
            {
                throw new InvalidDataException("An actions page uses an unsupported capture encoding.", exception);
            }
            var actions = CongressBillActionsParser.Parse(response.Body, response.Request, source.SourceUrl, expected);
            var shouldClose = index == pages.Count - 1;
            if (pageRef.ClosesChain != shouldClose || (actions.Next is null) != shouldClose)
                throw new InvalidDataException("The saved actions chain must close exactly on its final captured page.");
            previousNext = actions.Next;
            parsed.Add(new VerifiedBillActionsPage(pageRef, actions));
        }

        if (previousNext is not null) throw new InvalidDataException("The saved actions chain is missing its closing page.");
        return await store.RecordVerifiedActionChainAsync(detailJobId, parsed, cancellationToken);
    }
}
