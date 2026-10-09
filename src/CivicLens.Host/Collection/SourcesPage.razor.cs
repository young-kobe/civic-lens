using CivicLens.Application.Collection;
using CivicLens.Application.Collection.Health;
using CivicLens.Application.Collection.Jobs;
using CivicLens.Application.Review;
using CivicLens.Core.Review;
using CivicLens.Host.Components.Layout;
using CivicLens.Host.Components.Ui;
using CivicLens.Host.Review;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;

namespace CivicLens.Host.Collection;

public sealed partial class SourcesPage : IAsyncDisposable
{
    public const int SourcePageSize = 10;
    private static readonly TimeSpan ActiveRefresh = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan IdleRefresh = TimeSpan.FromSeconds(30);
    private static readonly IReadOnlyList<Crumb> Crumbs = [new("Sources")];

    private readonly CancellationTokenSource disposal = new();
    private PeriodicTimer? refresh;
    private readonly Dictionary<string, string> requestKeys = new(StringComparer.Ordinal);
    private HashSet<string>? knownJobIds;
    private HashSet<string> newJobIds = new(StringComparer.Ordinal);
    private CollectionWorkspace? workspace;
    private ReviewActor? actor;
    private IReadOnlyList<SourceRow>? rows;
    private CollectionWorkspaceSources? sources;
    private string? selectedJobId;
    private string? query;
    private bool attentionOnly;
    private int sourcePage;
    private bool busy;
    private PageNotice? notice;

    [Inject] private ReviewActorAccessor Actors { get; set; } = default!;
    [Inject] private IServiceProvider Services { get; set; } = default!;
    [Inject] private NavigationManager Navigation { get; set; } = default!;
    [CascadingParameter] private Task<AuthenticationState> AuthenticationState { get; set; } = default!;
    [SupplyParameterFromQuery(Name = "jobs")] private string? JobsCursor { get; set; }
    [SupplyParameterFromQuery(Name = "jobId")] private string? JobId { get; set; }

    public static bool IsActive(CollectionJobRecord job) =>
        job.State is CollectionJobState.Pending or CollectionJobState.Running or CollectionJobState.WaitingToRetry;

    protected override async Task OnInitializedAsync()
    {
        actor = Actors.GetActor((await AuthenticationState).User);
        if (actor.Role != ReviewRole.Owner)
        {
            Navigation.NavigateTo("/access-denied", forceLoad: true);
            return;
        }
        workspace = Services.GetService<CollectionWorkspace>();
        if (workspace is null)
        {
            notice = new("Source checks are not set up. Set the collection configuration on the server, then restart the workspace.", Tone.Bad);
            return;
        }
        selectedJobId = JobId;
        await LoadAsync();
    }

    protected override void OnAfterRender(bool firstRender)
    {
        if (!firstRender || !RendererInfo.IsInteractive || workspace is null) return;
        refresh = new PeriodicTimer(RefreshPeriod);
        _ = RefreshAsync(refresh, disposal.Token);
    }

    public async ValueTask DisposeAsync()
    {
        await disposal.CancelAsync();
        refresh?.Dispose();
        disposal.Dispose();
    }

    private async Task RefreshAsync(PeriodicTimer timer, CancellationToken cancellationToken)
    {
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                await InvokeAsync(async () =>
                {
                    if (!busy) await LoadAsync();
                    StateHasChanged();
                });
            }
        }
        catch (OperationCanceledException) { }
    }

    private TimeSpan RefreshPeriod => AnyCheckActive ? ActiveRefresh : IdleRefresh;

    private bool AnyCheckActive => sources is not null &&
        (sources.Jobs.Any(IsActive) || sources.Health.Sources.Any(item => item.State == SourceCheckState.Checking));

    private async Task LoadAsync()
    {
        try
        {
            sources = await workspace!.GetSourcesAsync(actor!, selectedJobId, JobsCursor, CollectionWorkspace.RecentJobLimit, disposal.Token);
            rows = SourceRow.From(workspace.GetConfiguration(actor!), sources.Health);
            TrackNewJobs(sources.Jobs);
            if (refresh is not null) refresh.Period = RefreshPeriod;
        }
        catch (OperationCanceledException) when (disposal.IsCancellationRequested) { }
        catch (ArgumentException)
        {
            selectedJobId = null;
            notice = new("This link no longer matches a saved check. Open Sources again to see the newest checks.", Tone.Warn);
        }
        catch (Exception)
        {
            notice = new("Sources could not be loaded. The page tries again on its own.", Tone.Bad);
        }
    }

    private void TrackNewJobs(IReadOnlyList<CollectionJobRecord> jobs)
    {
        var ids = jobs.Select(job => job.JobId).ToHashSet(StringComparer.Ordinal);
        newJobIds = knownJobIds is null ? new(StringComparer.Ordinal) : ids.Except(knownJobIds).ToHashSet(StringComparer.Ordinal);
        knownJobIds = ids;
    }

    private Task CheckNowAsync(SourceRow row) => RunAsync(async (service, owner) =>
    {
        var key = RequestKey("collect:" + row.SourceId);
        await service.EnqueueAsync(owner, row.SourceId, key, disposal.Token);
        requestKeys.Remove("collect:" + row.SourceId);
        return "Check queued. Its result appears in Recent checks.";
    });

    private Task StopAsync(string jobId) => RunAsync(async (service, owner) =>
    {
        var job = await service.CancelAsync(owner, jobId, disposal.Token);
        return job?.State is CollectionJobState.Succeeded or CollectionJobState.Failed
            ? "This check had already finished."
            : "Stop requested. The collector stops the check at the next safe point.";
    });

    private Task CheckAgainAsync(string jobId) => RunAsync(async (service, owner) =>
    {
        var key = RequestKey("recollect:" + jobId);
        await service.RecollectAsync(owner, jobId, key, disposal.Token);
        requestKeys.Remove("recollect:" + jobId);
        return "Check queued for this page. Its result appears in Recent checks.";
    });

    private async Task StartDraftAsync(string comparisonId)
    {
        if (busy) return;
        busy = true;
        try
        {
            var draft = await Services.GetRequiredService<CreateDocumentChangeDraft>().ExecuteAsync(actor!,
                new CreateDocumentChangeDraftRequest(comparisonId, RequestKey("draft:" + comparisonId)), disposal.Token);
            Navigation.NavigateTo($"/Review/{draft.DraftId}", forceLoad: true);
        }
        catch (OperationCanceledException) when (disposal.IsCancellationRequested) { }
        catch (ArgumentException)
        {
            notice = new("This change can no longer start a draft. Open Review to choose another change.", Tone.Warn);
        }
        catch (Exception)
        {
            notice = new("The draft could not be created. Try again.", Tone.Bad);
        }
        finally { busy = false; }
    }

    private async Task OpenDetailsAsync(string jobId)
    {
        selectedJobId = jobId;
        await LoadAsync();
    }

    private void CloseDetails()
    {
        selectedJobId = null;
        if (sources is not null) sources = sources with { SelectedJob = null };
    }

    private async Task RunAsync(Func<CollectionWorkspace, ReviewActor, Task<string>> action)
    {
        if (busy || workspace is null) return;
        busy = true;
        try
        {
            notice = new(await action(workspace, actor!), Tone.Ok);
            await LoadAsync();
        }
        catch (OperationCanceledException) when (disposal.IsCancellationRequested) { }
        catch (ArgumentException)
        {
            notice = new("That action no longer applies to this source. The list is up to date now.", Tone.Warn);
            await LoadAsync();
        }
        catch (Exception)
        {
            notice = new("The action could not be confirmed. Check the list before you try again.", Tone.Bad);
        }
        finally { busy = false; }
    }

    private string RequestKey(string action)
    {
        if (!requestKeys.TryGetValue(action, out var key))
        {
            key = Guid.NewGuid().ToString("N");
            requestKeys[action] = key;
        }
        return key;
    }

    private void DismissNotice() => notice = null;

    private void ShowAttention(bool value)
    {
        attentionOnly = value;
        ShowFirstPage();
    }

    private void ShowFirstPage() => sourcePage = 0;

    private void ShowPreviousPage() => sourcePage--;

    private void ShowNextPage() => sourcePage++;

    private IReadOnlyList<SourceRow> FilteredSources => (rows ?? [])
        .Where(row => !attentionOnly || row.Health.NeedsAttention)
        .Where(row => row.Matches(query)).ToArray();

    private IReadOnlyList<SourceRow> SourcePage => FilteredSources.Skip(sourcePage * SourcePageSize).Take(SourcePageSize).ToArray();

    private bool HasNextSourcePage => (sourcePage + 1) * SourcePageSize < FilteredSources.Count;

    private string SourcePageSummary
    {
        get
        {
            var total = FilteredSources.Count;
            if (total == 0) return "No sources to show";
            var first = sourcePage * SourcePageSize + 1;
            return $"Showing {first} to {Math.Min(first + SourcePageSize - 1, total)} of {total}";
        }
    }

    private string SourceCount => $"{rows?.Count ?? 0} configured";

    private int AttentionCount => sources?.Health.AttentionCount ?? 0;

    private string? RowClass(CollectionJobRecord job) => newJobIds.Contains(job.JobId) ? "is-new" : null;

    private static string Pressed(bool value) => value ? "true" : "false";

    private static string? JobsHref(string? cursor) => cursor is null ? null : "/Review/Sources?jobs=" + Uri.EscapeDataString(cursor);

    private sealed record PageNotice(string Text, Tone Tone);
}
