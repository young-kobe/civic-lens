using CivicLens.Application.Collection;
using CivicLens.Application.Collection.Jobs;
using CivicLens.Application.Collection.Processing;
using CivicLens.Application.Documents;
using CivicLens.Core.Review;
using CivicLens.Host.Review;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CivicLens.Host.Pages.Review;

[Authorize]
public sealed class SourcesModel(ReviewActorAccessor actors, CollectionWorkspace? workspace = null) : PageModel
{
    public CollectionConfiguration? Configuration { get; private set; }
    public IReadOnlyList<CollectionJobRecord> Jobs { get; private set; } = [];
    public IReadOnlyDictionary<string, SourceProcessingSummary> Processing { get; private set; } =
        new Dictionary<string, SourceProcessingSummary>(StringComparer.Ordinal);
    public CollectionJobRecord? SelectedJob { get; private set; }
    public DocumentHistory? History { get; private set; }
    public Dictionary<string, StoredCollectionAttempt> RetainedAttempts { get; } = new(StringComparer.Ordinal);
    public string? Error { get; private set; }
    public string? HistoryError { get; private set; }
    public string? Message { get; private set; }
    public bool IsConfigured => workspace is not null;
    [BindProperty] public string? RequestKey { get; set; } = Guid.NewGuid().ToString("N");
    [BindProperty] public string? SourceId { get; set; }

    public string SourceRequestKey(string sourceId) => SourceId == sourceId && RequestKey is not null ? RequestKey : Guid.NewGuid().ToString("N");

    public async Task<IActionResult> OnGetAsync(string? jobId, string? status, CancellationToken cancellationToken)
    {
        Message = status switch
        {
            "queued" => "Check queued. Articles, readable text, and comparisons will be prepared automatically.",
            "cancelled" => "Stopping requested. The background collector will finish stopping any active check.",
            "finished" => "This check has already finished.",
            "admitted" => "Articles queued for saving within this source's collection limits. Follow their progress in recent source checks.",
            "extracted" => "Readable document saved. Compare it with an earlier version to see what changed.",
            _ => null
        };
        return await RunAsync(async (service, actor) =>
        {
            await LoadAsync(service, actor, jobId, cancellationToken);
            return Page();
        }, jobId, cancellationToken, reloadOnFailure: false);
    }

    public Task<IActionResult> OnPostCollectAsync(string sourceId, string requestKey, CancellationToken cancellationToken) =>
        RunAsync(async (service, actor) =>
        {
            var job = await service.EnqueueAsync(actor, sourceId, requestKey, cancellationToken);
            return RedirectToPage(new { status = "queued" });
        }, null, cancellationToken);

    public Task<IActionResult> OnPostCancelAsync(string jobId, CancellationToken cancellationToken) =>
        RunAsync(async (service, actor) =>
        {
            var job = await service.CancelAsync(actor, jobId, cancellationToken);
            var status = job?.State is CollectionJobState.Succeeded or CollectionJobState.Failed ? "finished" : "cancelled";
            return RedirectToPage(new { status });
        }, jobId, cancellationToken);

    public Task<IActionResult> OnPostRecollectAsync(string jobId, string requestKey, CancellationToken cancellationToken) =>
        RunAsync(async (service, actor) =>
        {
            var job = await service.RecollectAsync(actor, jobId, requestKey, cancellationToken);
            return RedirectToPage(new { status = "queued" });
        }, jobId, cancellationToken);

    public Task<IActionResult> OnPostAdmitAsync(string jobId, string attemptId, string requestKey, CancellationToken cancellationToken) =>
        RunAsync(async (service, actor) =>
        {
            _ = await service.AdmitArticlesAsync(actor, jobId, attemptId, requestKey, cancellationToken);
            return RedirectToPage(new { jobId, status = "admitted" });
        }, jobId, cancellationToken);

    public Task<IActionResult> OnPostExtractAsync(string jobId, string attemptId, CancellationToken cancellationToken) =>
        RunAsync(async (service, actor) =>
        {
            _ = await service.ExtractAsync(actor, jobId, attemptId, cancellationToken);
            return RedirectToPage(new { jobId, status = "extracted" });
        }, jobId, cancellationToken);

    public Task<IActionResult> OnPostCompareAsync(string jobId, string beforeId, string afterId, CancellationToken cancellationToken) =>
        RunAsync(async (service, actor) =>
        {
            var result = await service.CompareAsync(actor, jobId, beforeId, afterId, cancellationToken);
            return RedirectToPage("/Documents/Comparison", new { comparisonId = result.ComparisonId });
        }, jobId, cancellationToken);

    public Task<IActionResult> OnGetActivityAsync(CancellationToken cancellationToken) =>
        RunAsync(async (service, actor) =>
        {
            await LoadAsync(service, actor, null, cancellationToken);
            return new JsonResult(new
            {
                jobs = Jobs.Select(job => new
                {
                    jobId = job.JobId,
                    title = DocumentLabel(job.Definition.Url),
                    sourceId = job.Definition.SourceId,
                    sourceName = JobSourceLabel(job),
                    createdAt = job.CreatedAt.ToString("yyyy-MM-dd HH:mm 'UTC'"),
                    progress = Processing.GetValueOrDefault(job.JobId),
                    label = StateLabel(job),
                    isActive = job.State is CollectionJobState.Pending or CollectionJobState.Running or CollectionJobState.WaitingToRetry,
                    canStop = job.State is CollectionJobState.Pending or CollectionJobState.Running or CollectionJobState.WaitingToRetry
                }),
                sources = Configuration!.Sources.Select(source =>
                {
                    var jobs = Jobs.Where(job => job.Definition.SourceId == source.Id && job.Definition.Url == source.Url).ToArray();
                    return new
                    {
                        sourceId = source.Id,
                        enabled = source.Enabled,
                        busy = jobs.Any(job => job.State is CollectionJobState.Pending or CollectionJobState.Running or CollectionJobState.WaitingToRetry) ||
                            jobs.Any(job => Processing.GetValueOrDefault(job.JobId)?.IsActive == true),
                        progress = jobs.Length == 0 ? null : Processing.GetValueOrDefault(jobs[0].JobId),
                        label = jobs.Length == 0 ? "No check yet" : StateLabel(jobs[0])
                    };
                })
            });
        }, null, cancellationToken, reloadOnFailure: false);

    private async Task<IActionResult> RunAsync(Func<CollectionWorkspace, ReviewActor, Task<IActionResult>> operation,
        string? jobId, CancellationToken cancellationToken, bool reloadOnFailure = true)
    {
        ReviewActor actor;
        try { actor = actors.GetActor(User); }
        catch (UnauthorizedAccessException) { return Forbid(); }
        if (actor.Role != ReviewRole.Owner) return Forbid();
        if (workspace is null)
        {
            Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            Error = "Source controls are not configured. Set the collection configuration and capture directory on the server, then restart review.";
            return Page();
        }
        try
        {
            if (!ModelState.IsValid) throw new ArgumentException();
            return await operation(workspace, actor);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (ArgumentException)
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
            Error = "The selection is invalid or no longer matches its saved source settings. Refresh, choose a current source or two compatible observations, and try again.";
        }
        catch (Exception)
        {
            Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            Error = "The operation could not be confirmed. Refresh and inspect the saved job or evidence before retrying.";
        }
        if (reloadOnFailure)
        {
            try { await LoadAsync(workspace, actor, jobId, cancellationToken); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception) { }
        }
        return Page();
    }

    private async Task LoadAsync(CollectionWorkspace service, ReviewActor actor, string? jobId, CancellationToken cancellationToken)
    {
        Configuration = service.GetConfiguration(actor);
        var result = await service.GetSourcesAsync(actor, jobId, null, CollectionWorkspace.RecentJobLimit, cancellationToken);
        Jobs = result.Jobs;
        Processing = BuildProcessing(result);
        SelectedJob = result.SelectedJob;
        foreach (var retained in result.RetainedAttempts) RetainedAttempts.Add(retained.Key, retained.Value);
        History = result.History;
        if (result.HistoryLimitExceeded)
            HistoryError = "This document history exceeds the safe display limit. It has not been partially loaded.";
        else if (result.HistoryUnavailable)
            HistoryError = "Document history is temporarily unavailable. Refresh to retry.";
    }

    private static IReadOnlyDictionary<string, SourceProcessingSummary> BuildProcessing(CollectionWorkspaceSources result)
    {
        var records = (result.Processing ?? []).ToLookup(record => record.JobId, StringComparer.Ordinal);
        var summaries = new Dictionary<string, SourceProcessingSummary>(StringComparer.Ordinal);
        foreach (var job in result.Jobs.Where(job => job.State == CollectionJobState.Succeeded))
        {
            var pages = records[job.JobId].Where(record => record.AttemptId != "prepare").ToArray();
            var record = pages.FirstOrDefault(record => record.Outcome == EvidenceProcessingOutcome.Changed)
                ?? pages.FirstOrDefault(record => record.Status is EvidenceProcessingStatus.Pending or EvidenceProcessingStatus.Running or
                    EvidenceProcessingStatus.RetryWaiting or EvidenceProcessingStatus.WaitingForPredecessor)
                ?? pages.FirstOrDefault() ?? records[job.JobId].FirstOrDefault();
            summaries[job.JobId] = DescribeProcessing(job, record);
        }
        return summaries;
    }

    private static SourceProcessingSummary DescribeProcessing(CollectionJobRecord job, EvidenceProcessingRecord? record)
    {
        if (record is null) return new("Preparing", "Waiting for the background worker.", null, false, true);
        if (record.Status is EvidenceProcessingStatus.Failed or EvidenceProcessingStatus.Blocked)
            return new("Needs attention", record.ErrorCode switch
            {
                "configurationUnavailable" => "This check has no retained processing settings.",
                "contentProfileUnavailable" => "A readable text profile is needed for this source.",
                "historyLimitExceeded" => "The document history exceeds processing limits.",
                "comparisonLimitExceeded" => "The change exceeds comparison limits.",
                _ => "Preparation could not finish. Open Details to inspect the evidence."
            }, null, true, false);
        if (record.Status == EvidenceProcessingStatus.RetryWaiting)
            return new("Waiting to retry", "Preparation will resume automatically.", null, false, true);
        if (record.Status == EvidenceProcessingStatus.WaitingForPredecessor)
            return new("Preparing comparison", "Waiting for earlier evidence to finish preparing.", null, false, true);
        if (record.Status is EvidenceProcessingStatus.Pending or EvidenceProcessingStatus.Running)
            return new(record.Stage switch
            {
                EvidenceProcessingStage.Preparation => "Preparing",
                EvidenceProcessingStage.Comparison => "Comparing",
                _ => "Preparing text"
            },
                "No action needed.", null, false, true);
        if (job.Definition.Mode is CivicLens.Collection.Contracts.CollectionMode.Feed or CivicLens.Collection.Contracts.CollectionMode.Html)
            return new("Articles queued", $"{record.AdmittedCount} queued · {record.DeferredCount} outside this check's limits · {record.DuplicateCount} already active.", null, false, false);
        return record.Outcome switch
        {
            EvidenceProcessingOutcome.Changed => new("Ready for review", "Compare the wording and draft an account.", record.ComparisonId, false, false),
            EvidenceProcessingOutcome.Baseline => new("Baseline saved", record.ErrorCode is "historyGap" or "incompatibleHistory"
                ? "A new baseline follows a gap or changed processing settings. No source edit was inferred."
                : "Readable text retained. A later check can detect changes.", null, false, false),
            EvidenceProcessingOutcome.Prepared => new("Check complete", "No new capture was produced. Earlier evidence remains available.", null, false, false),
            _ => new("No change", "The readable text matches the earlier version.", null, false, false)
        };
    }

    public string SourceLabel(WatchedSourceConfiguration source)
    {
        var names = Configuration!.People.Where(person => source.Coverage?.Any(item => item.PersonId == person.Id) == true ||
            source.PersonIds?.Contains(person.Id) == true).Select(person => person.Name);
        var label = string.Join(", ", names);
        return label.Length == 0 ? DocumentLabel(source.Url) : label;
    }

    public string JobSourceLabel(CollectionJobRecord job)
    {
        var source = Configuration?.Sources.FirstOrDefault(source => source.Id == job.Definition.SourceId);
        return source is null ? "Retained source" : SourceLabel(source);
    }

    public static string DocumentLabel(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed)) return url;
        return Uri.UnescapeDataString(parsed.AbsolutePath.Trim('/').Split('/').LastOrDefault() ?? parsed.Host).Replace('-', ' ');
    }

    public static string StateLabel(CollectionJobRecord job) => job.CancellationRequested && job.State == CollectionJobState.Running
        ? "Cancellation requested" : job.State switch
        {
            CollectionJobState.Pending => "Queued",
            CollectionJobState.Running => "Collecting",
            CollectionJobState.WaitingToRetry => "Waiting to retry",
            CollectionJobState.Succeeded => "Collection complete",
            CollectionJobState.Cancelled => "Cancelled",
            _ => "Collection failed"
        };
}
