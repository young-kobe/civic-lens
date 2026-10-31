using CivicLens.Application.Publication;
using CivicLens.Core.Review;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CivicLens.Host.Review;

public abstract class PublishedRecordEditorPage : ReviewRecordPage
{
    protected static readonly ReviewFailureMessages TakeOffFailures = new(
        "The revision or review state changed after you opened this page. Check the latest state, then take the record off site again.",
        "The request is not valid. Reload the page and try again.",
        "The record could not be taken off the site. Press Take off site again, or reload the page to see the current state.");

    private readonly string newTakeOffKey = Guid.NewGuid().ToString("N");

    protected string? takeOffRefusal;

    [Inject] protected IServiceProvider Services { get; set; } = default!;

    [SupplyParameterFromForm(FormName = TakeOffSitePanel.FormName, Name = "expectedRevisionNumber")] protected int TakeOffExpectedRevision { get; set; }
    [SupplyParameterFromForm(FormName = TakeOffSitePanel.FormName, Name = "expectedReviewStateVersion")] protected int TakeOffExpectedStateVersion { get; set; }
    [SupplyParameterFromForm(FormName = TakeOffSitePanel.FormName, Name = "idempotencyKey")] protected string? TakeOffKey { get; set; }
    [SupplyParameterFromForm(FormName = TakeOffSitePanel.FormName, Name = "note")] protected string? TakeOffNote { get; set; }

    protected abstract PublishedRecordReference TakeOffRecord { get; }

    protected TakeOffSiteView TakeOffView { get; private set; } = TakeOffSiteView.Hidden;

    protected string TakeOffFormKey => TakeOffKey ?? newTakeOffKey;

    protected override async Task OnInitializedAsync()
    {
        await base.OnInitializedAsync();
        await LoadTakeOffViewAsync();
    }

    protected override string? StatusText() => Status == TakeOffSitePanel.DoneStatus
        ? "The record is off the public site. Its page now shows a notice."
        : base.StatusText();

    protected async Task<string?> RunTakeOffSiteAsync(ReviewStatus status, Func<ReviewDecisionKind, string, Task> recordDecision,
        Func<Task> reload)
    {
        string? refusal = null;
        var target = await RunCommandAsync(async () =>
        {
            var request = new TakeOffSiteRequest(TakeOffRecord, status, TakeOffNote ?? "", TakeOffKey ?? "");
            refusal = await Services.GetRequiredService<TakeOffSite>().ExecuteAsync(actor, request, recordDecision, RequestAborted);
            return RecordRoutes.Editor(TakeOffRecord.Kind, TakeOffRecord.RecordId, TakeOffSitePanel.DoneStatus);
        }, TakeOffFailures, reload);
        if (target is not null && refusal is null) return target;
        if (refusal is not null)
        {
            takeOffRefusal = refusal;
            HttpContext.Response.StatusCode = StatusCodes.Status409Conflict;
            await reload();
        }
        await LoadTakeOffViewAsync();
        return null;
    }

    private async Task LoadTakeOffViewAsync()
    {
        if (actor.Role != ReviewRole.Owner) return;
        if (Services.GetService<TakeOffSite>() is not { } takeOff)
        {
            TakeOffView = new(true, false, false, TakeOffSiteStatus.NotPublished);
            return;
        }
        try
        {
            TakeOffView = new(true, true, false, await takeOff.GetStatusAsync(actor, TakeOffRecord, RequestAborted));
        }
        catch (OperationCanceledException) when (RequestAborted.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            Logger.LogError(exception, "The take off site check failed.");
            TakeOffView = new(true, true, true, TakeOffSiteStatus.NotPublished);
        }
    }
}
