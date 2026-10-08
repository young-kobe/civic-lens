using CivicLens.Application.Collection;
using CivicLens.Application.Collection.Jobs;
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
            "queued" => "Collection queued. The worker will run it when its budget and source pacing allow.",
            "cancelled" => "Cancellation requested. An active attempt stops when the worker observes the request.",
            "admitted" => "The bounded article batch was admitted. Check recent jobs for the selected articles.",
            "extracted" => "Text retained. Inspect the extraction before comparing or drafting.",
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
            return RedirectToPage(new { jobId = job.JobId, status = "queued" });
        }, null, cancellationToken);

    public Task<IActionResult> OnPostCancelAsync(string jobId, CancellationToken cancellationToken) =>
        RunAsync(async (service, actor) =>
        {
            _ = await service.CancelAsync(actor, jobId, cancellationToken);
            return RedirectToPage(new { jobId, status = "cancelled" });
        }, jobId, cancellationToken);

    public Task<IActionResult> OnPostRecollectAsync(string jobId, string requestKey, CancellationToken cancellationToken) =>
        RunAsync(async (service, actor) =>
        {
            var job = await service.RecollectAsync(actor, jobId, requestKey, cancellationToken);
            return RedirectToPage(new { jobId = job.JobId, status = "queued" });
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
        var result = await service.GetSourcesAsync(actor, jobId, cancellationToken);
        Jobs = result.Jobs;
        SelectedJob = result.SelectedJob;
        foreach (var retained in result.RetainedAttempts) RetainedAttempts.Add(retained.Key, retained.Value);
        History = result.History;
        if (result.HistoryLimitExceeded)
            HistoryError = "This document history exceeds the safe display limit. It has not been partially loaded.";
        else if (result.HistoryUnavailable)
            HistoryError = "Document history is temporarily unavailable. Refresh to retry.";
    }

    public string SourceLabel(WatchedSourceConfiguration source)
    {
        var names = Configuration!.People.Where(person => source.Coverage?.Any(item => item.PersonId == person.Id) == true ||
            source.PersonIds?.Contains(person.Id) == true).Select(person => person.Name);
        return string.Join(", ", names);
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
            CollectionJobState.Running => "Collecting or awaiting recovery",
            CollectionJobState.WaitingToRetry => "Waiting to retry",
            CollectionJobState.Succeeded => "Collection complete",
            CollectionJobState.Cancelled => "Cancelled",
            _ => "Collection failed"
        };
}
