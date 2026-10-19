using CivicLens.Application.Review;
using CivicLens.Core.Review;
using Microsoft.AspNetCore.Components;

namespace CivicLens.Host.Review;

public abstract class TriagePage : DraftListPage
{
    protected const string DecideForm = "decide";
    private const string Approved = "approved";
    private const string ChangesRequested = "changes-requested";

    private static readonly ReviewFailureMessages DecideFailures = new(
        "A record changed after you opened this page. The page shows the latest state. Check it before you decide again.",
        "The decision needs valid details. Requesting changes needs a note. Approval needs a passing check and no open concern.",
        "The decision could not be saved. Try again.");

    protected readonly string renderKey = Guid.NewGuid().ToString("N");
    protected string? statusMessage;

    [Inject] private NavigationManager Navigation { get; set; } = default!;

    [SupplyParameterFromQuery(Name = "decided")] private string? Decided { get; set; }

    [SupplyParameterFromForm(FormName = DecideForm, Name = "approve")] private string? ApproveTarget { get; set; }
    [SupplyParameterFromForm(FormName = DecideForm, Name = "requestChanges")] private string? RequestChangesTarget { get; set; }
    [SupplyParameterFromForm(FormName = DecideForm, Name = "notes")] private Dictionary<string, string>? Notes { get; set; }
    [SupplyParameterFromForm(FormName = DecideForm, Name = "idempotencyKey")] private string? DecideKey { get; set; }

    protected abstract string Route { get; }

    protected abstract Task LoadAsync();

    protected abstract Task DecideRecordAsync(BulkApprovalTarget target, ReviewDecisionKind kind, string? note, string idempotencyKey);

    protected override async Task OnInitializedAsync()
    {
        await ReadActorAsync();
        if (HttpMethods.IsPost(HttpContext.Request.Method)) return;
        statusMessage = Decided switch
        {
            Approved => "The record is approved.",
            ChangesRequested => "Changes are requested on the record.",
            _ => null
        };
        await LoadAsync();
    }

    protected string TriageHref(string? cursor, params string?[] otherParts) => Href(Route, null, cursor, "drafts", otherParts);

    protected string? PagerHref(string? cursor) => cursor is null ? null : TriageHref(cursor);

    protected async Task DecideSelectedAsync() =>
        await RunAsync(RecordDecisionAsync, DecideFailures, href =>
        {
            Navigation.NavigateTo(href);
            return Task.CompletedTask;
        });

    protected async Task RunAsync<T>(Func<Task<T>> command, ReviewFailureMessages failures, Func<T, Task> onSuccess)
    {
        var (result, failure) = await ReviewCommand.RunAsync(command, HttpContext.Response, RequestAborted);
        switch (failure)
        {
            case ReviewCommandFailure.None:
                await onSuccess(result!);
                return;
            case ReviewCommandFailure.AccessDenied:
                Navigation.NavigateTo(ReviewCommand.AccessDenied);
                return;
            default:
                error = failure switch
                {
                    ReviewCommandFailure.Conflict => failures.Conflict,
                    ReviewCommandFailure.Invalid => failures.Invalid,
                    _ => failures.Unavailable
                };
                await LoadAsync();
                return;
        }
    }

    private async Task<string> RecordDecisionAsync()
    {
        var approving = ApproveTarget is not null;
        var target = TriageTargets.Parse(ApproveTarget ?? RequestChangesTarget ?? throw new ArgumentException("Choose a decision."));
        var note = approving ? null : Notes?.GetValueOrDefault(target.RecordId);
        await DecideRecordAsync(target, approving ? ReviewDecisionKind.Approve : ReviewDecisionKind.RequestChanges, note,
            $"decide:{DecideKey}:{target.RecordId}");
        return TriageHref(DraftsCursor, Part("decided", approving ? Approved : ChangesRequested));
    }
}
