using Microsoft.Extensions.Logging;
using System.Collections.Immutable;
using CivicLens.Application.Issues;
using CivicLens.Application.Statements;
using CivicLens.Core.Review;
using CivicLens.Core.Statements;
using CivicLens.Host.Components.Layout;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;

namespace CivicLens.Host.Review;

public sealed partial class GateLabelEditor
{
    private const string LabelForm = "label";
    private const string Yes = "yes";
    private const string No = "no";

    private ReviewActor? actor;
    private GateLabelTask? task;
    private string? error;
    private bool notFound;

    [Inject] private ReviewActorAccessor Actors { get; set; } = default!;
    [Inject] private ILogger<GateLabelEditor> Logger { get; set; } = default!;
    [Inject] private GetGateLabelTask GetTask { get; set; } = default!;
    [Inject] private SaveGateLabel SaveLabel { get; set; } = default!;
    [Inject] private ListGateLabelQueue ListQueue { get; set; } = default!;
    [Inject] private IssueConfiguration Issues { get; set; } = default!;
    [Inject] private NavigationManager Navigation { get; set; } = default!;

    [CascadingParameter] private HttpContext HttpContext { get; set; } = default!;
    [CascadingParameter] private Task<AuthenticationState> AuthenticationState { get; set; } = default!;

    [Parameter] public string CandidateId { get; set; } = string.Empty;

    [SupplyParameterFromQuery(Name = "status")] private string? Status { get; set; }

    [SupplyParameterFromForm(FormName = LabelForm, Name = "extractionId")] private string? ExtractionId { get; set; }
    [SupplyParameterFromForm(FormName = LabelForm)] private LabelInput? Input { get; set; }

    private CancellationToken RequestAborted => HttpContext.RequestAborted;

    private string? StatusMessage => Status == "saved" && !HttpMethods.IsPost(HttpContext.Request.Method)
        ? "The label is saved. This is the next release." : null;

    private IReadOnlyList<Crumb> Crumbs => [new("Review", "/Review"), new("Gate labels", GateLabelRoutes.List), new(task is null ? "Release" : "Label")];

    protected override async Task OnInitializedAsync()
    {
        actor = Actors.GetActor((await AuthenticationState).User);
        if (actor.Role != ReviewRole.Owner)
        {
            Navigation.NavigateTo(GateLabelRoutes.AccessDenied, forceLoad: true);
            return;
        }
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            task = await GetTask.ExecuteAsync(actor!, CandidateId, RequestAborted);
        }
        catch (OperationCanceledException) when (RequestAborted.IsCancellationRequested) { throw; }
        catch (ArgumentException) { }
        catch (Exception exception)
        {
            Logger.LogError(exception, "The gate label release could not load.");
            HttpContext.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            error = "The release could not load. Refresh the page to try again.";
            return;
        }
        if (task is null)
        {
            notFound = true;
            HttpContext.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }
        if (!HttpMethods.IsPost(HttpContext.Request.Method)) Input = LabelInput.From(task.HumanLabel ?? task.ModelLabel);
    }

    private async Task SaveAsync()
    {
        if (task is null) return;
        var target = await TrySaveAsync();
        if (target is not null) Navigation.NavigateTo(target);
    }

    private async Task<string?> TrySaveAsync()
    {
        try
        {
            await SaveLabel.ExecuteAsync(actor!, BuildRequest(), RequestAborted);
            return await NextTargetAsync();
        }
        catch (OperationCanceledException) when (RequestAborted.IsCancellationRequested) { throw; }
        catch (UnauthorizedAccessException) { return GateLabelRoutes.AccessDenied; }
        catch (ArgumentException)
        {
            HttpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
            error = "Choose yes or no, and tick only listed issues, then save again.";
        }
        catch (Exception exception)
        {
            Logger.LogError(exception, "A gate label could not be saved.");
            HttpContext.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            error = "The label could not be saved. Your choices are kept. Try again.";
        }
        return null;
    }

    private SaveGateLabelRequest BuildRequest() => new(CandidateId, ExtractionId ?? "", [.. Input?.IssueIds ?? []],
        Input?.NamesBillOrVote switch
        {
            Yes => true,
            No => false,
            _ => throw new ArgumentException("Choose yes or no.")
        });

    private async Task<string> NextTargetAsync()
    {
        try
        {
            var next = (await ListQueue.ExecuteAsync(actor!, 1, RequestAborted)).Items.FirstOrDefault();
            return next is null ? GateLabelRoutes.List + "?status=done" : GateLabelRoutes.Editor(next.CandidateId, "saved");
        }
        catch (OperationCanceledException) when (RequestAborted.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            Logger.LogError(exception, "The next gate label could not be found.");
            return GateLabelRoutes.List;
        }
    }

    public sealed class LabelInput
    {
        public string[]? IssueIds { get; set; } = [];
        public string? NamesBillOrVote { get; set; }

        internal static LabelInput From(GateLabel? label) => label is null
            ? new()
            : new() { IssueIds = [.. label.IssueIds], NamesBillOrVote = label.NamesBillOrVote ? Yes : No };
    }
}
