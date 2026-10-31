using CivicLens.Application.Publication;
using CivicLens.Application.Review;
using CivicLens.Core.Review;
using CivicLens.Publication.Contracts;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CivicLens.Host.Review;

public abstract class ReviewRecordPage : ComponentBase
{
    protected const string SaveForm = "save";
    protected const string DecideForm = "decide";

    protected static readonly ReviewFailureMessages SaveFailures = new(
        "Someone saved a newer revision. Your wording is kept below. Compare both versions, then save again.",
        "Some fields or selections are not valid. Correct them and save again.",
        "The revision could not be saved. Your wording is kept in the form. Try again.");

    protected static readonly ReviewFailureMessages DecideFailures = new(
        "The revision or review state changed after you opened this page. Check the latest state before you record a decision.",
        "The decision needs valid details. Requesting changes and withdrawing approval need a reason. Approval must resolve every open concern.",
        "The decision could not be saved. The current review state shows below. Try again.");

    protected static readonly ReviewFailureMessages TakeOffFailures = new(
        "The revision or review state changed after you opened this page. Check the latest state, then take the record off site again.",
        "Give a reason of 1 to 500 characters.",
        "The record could not be taken off the site. The current state shows below. Try again.");

    protected readonly string saveKey = Guid.NewGuid().ToString("N");
    protected readonly string decisionKey = Guid.NewGuid().ToString("N");
    private readonly string newTakeOffKey = Guid.NewGuid().ToString("N");
    protected ReviewActor actor = default!;
    protected string? error;
    protected string? statusMessage;
    protected bool isConflict;
    protected bool hasUnsavedInput;
    protected bool notFound;
    protected string? takeOffRefusal;

    [Inject] protected ReviewActorAccessor Actors { get; set; } = default!;
    [Inject] protected NavigationManager Navigation { get; set; } = default!;
    [Inject] protected IServiceProvider Services { get; set; } = default!;
    [Inject] protected ILogger<ReviewRecordPage> Logger { get; set; } = default!;

    [CascadingParameter] protected HttpContext HttpContext { get; set; } = default!;
    [CascadingParameter] protected Task<AuthenticationState> AuthenticationState { get; set; } = default!;

    [SupplyParameterFromQuery(Name = "status")] protected string? Status { get; set; }

    [SupplyParameterFromForm(FormName = SaveForm, Name = "expectedRevisionNumber")] protected int SaveExpectedRevision { get; set; }
    [SupplyParameterFromForm(FormName = SaveForm, Name = "idempotencyKey")] protected string? SaveKey { get; set; }
    [SupplyParameterFromForm(FormName = DecideForm)] protected ReviewDecisionInput? Decision { get; set; }
    [SupplyParameterFromForm(FormName = DecideForm, Name = "expectedRevisionNumber")] protected int DecideExpectedRevision { get; set; }
    [SupplyParameterFromForm(FormName = DecideForm, Name = "expectedReviewStateVersion")] protected int DecideExpectedStateVersion { get; set; }
    [SupplyParameterFromForm(FormName = DecideForm, Name = "idempotencyKey")] protected string? DecideKey { get; set; }
    [SupplyParameterFromForm(FormName = DecideForm, Name = "kind")] protected string? Kind { get; set; }

    [SupplyParameterFromForm(FormName = TakeOffSitePanel.FormName, Name = "expectedRevisionNumber")] protected int TakeOffExpectedRevision { get; set; }
    [SupplyParameterFromForm(FormName = TakeOffSitePanel.FormName, Name = "expectedReviewStateVersion")] protected int TakeOffExpectedStateVersion { get; set; }
    [SupplyParameterFromForm(FormName = TakeOffSitePanel.FormName, Name = "idempotencyKey")] protected string? TakeOffKey { get; set; }
    [SupplyParameterFromForm(FormName = TakeOffSitePanel.FormName, Name = "note")] protected string? TakeOffNote { get; set; }

    protected string TakeOffFormKey => TakeOffKey ?? newTakeOffKey;
    protected CancellationToken RequestAborted => HttpContext.RequestAborted;
    protected bool CanDecide => !hasUnsavedInput && !isConflict && error is null;
    protected string SelectedTab => hasUnsavedInput || isConflict ? "edit" : "saved";

    protected abstract Task LoadAsync();

    protected override async Task OnInitializedAsync()
    {
        await ReadActorAsync();
        if (!HttpMethods.IsPost(HttpContext.Request.Method)) statusMessage = StatusText();
        await LoadAsync();
    }

    protected virtual string? StatusText() => Status switch
    {
        "saved" => "The new revision is saved. Review it before you record a decision.",
        "decided" => "Your decision on this revision is saved.",
        TakeOffSitePanel.DoneStatus => "The record is off the public site. Its page now shows a notice.",
        _ => null
    };

    protected async Task ReadActorAsync() => actor = Actors.GetActor((await AuthenticationState).User);

    protected virtual void ShowNotFound()
    {
        notFound = true;
        HttpContext.Response.StatusCode = StatusCodes.Status404NotFound;
    }

    protected async Task LoadGuardedAsync(Func<Task> load)
    {
        try
        {
            await load();
        }
        catch (OperationCanceledException) when (RequestAborted.IsCancellationRequested) { throw; }
        catch (UnauthorizedAccessException) { Navigation.NavigateTo(ReviewCommand.AccessDenied); }
        catch (ArgumentException) { ShowNotFound(); }
        catch (Exception exception)
        {
            Logger.LogError(exception, "The review workspace could not load a record.");
            HttpContext.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            error ??= "The review workspace could not load this record. Refresh the page to try again.";
        }
    }

    protected async Task<string?> RunCommandAsync(Func<Task<string>> command, ReviewFailureMessages messages,
        Func<Task> reloadAfterConflict)
    {
        var (target, failure) = await ReviewCommand.RunAsync(command, HttpContext.Response, Logger, RequestAborted);
        switch (failure)
        {
            case ReviewCommandFailure.AccessDenied:
                return ReviewCommand.AccessDenied;
            case ReviewCommandFailure.Conflict:
                isConflict = true;
                statusMessage = messages.Conflict;
                await reloadAfterConflict();
                break;
            case ReviewCommandFailure.Invalid:
                error = messages.Invalid;
                break;
            case ReviewCommandFailure.Unavailable:
                error = messages.Unavailable;
                break;
        }
        return target;
    }

    protected async Task<string?> RunTakeOffSiteAsync(PublishedRecordKind kind, string recordId, ReviewStatus status,
        Func<ReviewDecisionKind, string, Task> recordDecision, string doneTarget, Func<Task> reload)
    {
        string? refusal = null;
        var target = await RunCommandAsync(async () =>
        {
            var request = new TakeOffSiteRequest(new(kind, recordId), status, TakeOffNote ?? "", TakeOffKey ?? "");
            refusal = await Services.GetRequiredService<TakeOffSite>().ExecuteAsync(actor, request, recordDecision, RequestAborted);
            return doneTarget;
        }, TakeOffFailures, reload);
        if (refusal is null) return target;
        takeOffRefusal = refusal;
        HttpContext.Response.StatusCode = StatusCodes.Status409Conflict;
        await reload();
        return null;
    }

    protected void NavigateWhenSet(string? target)
    {
        if (target is not null) Navigation.NavigateTo(target);
    }

    protected static ReviewDecisionKind ParseDecisionKind(string? value)
    {
        if (!Enum.TryParse<ReviewDecisionKind>(value, ignoreCase: false, out var kind) || !Enum.IsDefined(kind))
            throw new ArgumentException("Choose a valid review action.");
        return kind;
    }

    protected string? CorrectionNoteOfApproval() =>
        ParseDecisionKind(Kind) == ReviewDecisionKind.Approve ? EmptyToNull(Decision?.CorrectionNote) : null;

    protected static string? EmptyToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
