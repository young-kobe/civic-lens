using CivicLens.Application.Review;
using CivicLens.Core.Review;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;

namespace CivicLens.Host.Review;

public abstract class ReviewRecordPage : ComponentBase
{
    protected const string SaveForm = "save";
    protected const string DecideForm = "decide";
    private const string AccessDenied = "/access-denied";

    protected static readonly ReviewFailureMessages SaveFailures = new(
        "Someone saved a newer revision. Your wording is kept below. Compare both versions, then save again.",
        "Some fields or selections are not valid. Correct them and save again.",
        "The revision could not be saved. Your wording is kept in the form. Try again.");

    protected static readonly ReviewFailureMessages DecideFailures = new(
        "The revision or review state changed after you opened this page. Check the latest state before you record a decision.",
        "The decision needs valid details. Requesting changes and withdrawing approval need a reason. Approval must resolve every open concern.",
        "The decision could not be saved. The current review state shows below. Try again.");

    protected readonly string saveKey = Guid.NewGuid().ToString("N");
    protected readonly string decisionKey = Guid.NewGuid().ToString("N");
    protected ReviewActor actor = default!;
    protected string? error;
    protected string? statusMessage;
    protected bool isConflict;
    protected bool hasUnsavedInput;
    protected bool notFound;

    [Inject] protected ReviewActorAccessor Actors { get; set; } = default!;
    [Inject] protected NavigationManager Navigation { get; set; } = default!;

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
        catch (UnauthorizedAccessException) { Navigation.NavigateTo(AccessDenied); }
        catch (ArgumentException) { ShowNotFound(); }
        catch (Exception)
        {
            HttpContext.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            error ??= "The review workspace could not load this record. Refresh the page to try again.";
        }
    }

    protected async Task<string?> RunCommandAsync(Func<Task<string>> command, ReviewFailureMessages messages,
        Func<Task> reloadAfterConflict)
    {
        try
        {
            return await command();
        }
        catch (OperationCanceledException) when (RequestAborted.IsCancellationRequested) { throw; }
        catch (UnauthorizedAccessException) { return AccessDenied; }
        catch (ReviewConflictException)
        {
            HttpContext.Response.StatusCode = StatusCodes.Status409Conflict;
            isConflict = true;
            statusMessage = messages.Conflict;
            await reloadAfterConflict();
        }
        catch (ArgumentException)
        {
            HttpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
            error = messages.Invalid;
        }
        catch (Exception)
        {
            HttpContext.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            error = messages.Unavailable;
        }
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

    protected static string? EmptyToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
