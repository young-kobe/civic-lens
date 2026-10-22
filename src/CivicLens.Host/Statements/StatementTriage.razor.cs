using CivicLens.Application.Statements;
using CivicLens.Core.Review;
using CivicLens.Host.Components.Layout;
using CivicLens.Host.Review;
using Microsoft.AspNetCore.Components;

namespace CivicLens.Host.Statements;

public sealed partial class StatementTriage
{
    private const string DecideForm = "decide";
    private const string Approved = "approved";
    private const string ChangesRequested = "changes-requested";

    private static readonly IReadOnlyList<Crumb> Crumbs = [new("Review", "/Review"), new("Statements", StatementRoutes.List), new("Triage")];

    private static readonly ReviewFailureMessages DecideFailures = new(
        "A record changed after you opened this page. The page shows the latest state. Check it before you decide again.",
        "The decision needs valid details. Requesting changes needs a note. Approval needs a passing check and no open concern.",
        "The decision could not be saved. Try again.");

    private readonly string renderKey = Guid.NewGuid().ToString("N");
    private StatementTriagePage? triage;
    private string? statusMessage;

    [Inject] private NavigationManager Navigation { get; set; } = default!;
    [Inject] private ListStatementTriage ListTriage { get; set; } = default!;
    [Inject] private DecideStatementReview DecideReview { get; set; } = default!;

    [SupplyParameterFromQuery(Name = "decided")] private string? Decided { get; set; }

    [SupplyParameterFromForm(FormName = DecideForm, Name = "approve")] private string? ApproveTarget { get; set; }
    [SupplyParameterFromForm(FormName = DecideForm, Name = "requestChanges")] private string? RequestChangesTarget { get; set; }
    [SupplyParameterFromForm(FormName = DecideForm, Name = "notes")] private Dictionary<string, string>? Notes { get; set; }
    [SupplyParameterFromForm(FormName = DecideForm, Name = "idempotencyKey")] private string? DecideKey { get; set; }

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

    private Task LoadAsync() => LoadGuardedAsync(
        async () => triage = await ListTriage.ExecuteAsync(actor, DraftsCursor, ReadListFilter(), RequestAborted),
        "The statement triage could not load. Refresh the page to try again.");

    private string? PagerHref(string? cursor) => cursor is null ? null : TriageHref(cursor);

    private string TriageHref(string? cursor, params string?[] otherParts) =>
        Href(StatementRoutes.Triage, null, cursor, "drafts", otherParts);

    private async Task DecideSelectedAsync()
    {
        var (href, failure) = await ReviewCommand.RunAsync(RecordDecisionAsync, HttpContext.Response, Logger, RequestAborted);
        switch (failure)
        {
            case ReviewCommandFailure.None:
                Navigation.NavigateTo(href!);
                return;
            case ReviewCommandFailure.AccessDenied:
                Navigation.NavigateTo(ReviewCommand.AccessDenied);
                return;
            default:
                error = failure switch
                {
                    ReviewCommandFailure.Conflict => DecideFailures.Conflict,
                    ReviewCommandFailure.Invalid => DecideFailures.Invalid,
                    _ => DecideFailures.Unavailable
                };
                await LoadAsync();
                return;
        }
    }

    private async Task<string> RecordDecisionAsync()
    {
        if (string.IsNullOrWhiteSpace(DecideKey)) throw new ArgumentException("The form needs an idempotency key.");
        var approving = ApproveTarget is not null;
        var target = TriageTargets.Parse(ApproveTarget ?? RequestChangesTarget ?? throw new ArgumentException("Choose a decision."));
        var kind = approving ? ReviewDecisionKind.Approve : ReviewDecisionKind.RequestChanges;
        var note = approving ? null : Notes?.GetValueOrDefault(target.RecordId);
        await DecideReview.ExecuteAsync(actor, new(target.RecordId, target.ExpectedRevisionNumber, target.ExpectedReviewStateVersion, kind, note,
            [], $"decide:{DecideKey}:{target.RecordId}"), RequestAborted);
        return TriageHref(DraftsCursor, Part("decided", approving ? Approved : ChangesRequested));
    }
}
