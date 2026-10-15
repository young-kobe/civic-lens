using System.Collections.Immutable;
using System.Globalization;
using CivicLens.Application.Actions;
using CivicLens.Application.Review;
using CivicLens.Core.Actions;
using CivicLens.Core.Review;
using CivicLens.Host.Components.Layout;
using CivicLens.Host.Components.Ui;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;

namespace CivicLens.Host.Review;

public sealed partial class ActionList
{
    private const int PageSize = 25;
    private const string ApproveForm = "approve-sponsorships";

    private static readonly IReadOnlyList<Crumb> Crumbs = [new("Review", "/Review"), new("Actions")];

    private readonly string renderKey = Guid.NewGuid().ToString("N");
    private ReviewActor? actor;
    private ActionReviewPage? drafts;
    private ActionReviewCounts? counts;
    private IReadOnlyList<ActionApprovalResult>? approvals;
    private string? error;

    [Inject] private ReviewActorAccessor Actors { get; set; } = default!;
    [Inject] private ListActionReviews ListDrafts { get; set; } = default!;
    [Inject] private GetActionReviewCounts GetCounts { get; set; } = default!;
    [Inject] private ApproveSponsorshipChanges Approve { get; set; } = default!;

    [CascadingParameter] private HttpContext HttpContext { get; set; } = default!;
    [CascadingParameter] private Task<AuthenticationState> AuthenticationState { get; set; } = default!;

    [SupplyParameterFromQuery(Name = "drafts")] private string? DraftsFilterName { get; set; }
    [SupplyParameterFromQuery(Name = "draftsCursor")] private string? DraftsCursor { get; set; }

    [SupplyParameterFromForm(FormName = ApproveForm, Name = "targets")] private string[]? Targets { get; set; }
    [SupplyParameterFromForm(FormName = ApproveForm, Name = "idempotencyKey")] private string? ApproveKey { get; set; }

    private DraftStatusFilter Filter => DraftFilterNames.Parse(DraftsFilterName);

    protected override async Task OnInitializedAsync()
    {
        actor = Actors.GetActor((await AuthenticationState).User);
        if (!HttpMethods.IsPost(HttpContext.Request.Method)) await LoadAsync();
    }

    private async Task LoadAsync()
    {
        var cancellationToken = HttpContext.RequestAborted;
        try
        {
            drafts = await ListDrafts.ExecuteAsync(actor!, DraftsCursor, PageSize, Filter, cancellationToken);
            counts = await GetCounts.ExecuteAsync(actor!, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (ArgumentException)
        {
            error = "This page link is no longer valid. Go back to the first page of the list.";
        }
        catch (Exception)
        {
            error = "The action list could not load. Refresh the page to try again.";
        }
    }

    private async Task ApproveAsync()
    {
        var cancellationToken = HttpContext.RequestAborted;
        try
        {
            approvals = await Approve.ExecuteAsync(actor!, new ApproveSponsorshipChangesRequest(ParseTargets(), ApproveKey ?? ""),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (ArgumentException)
        {
            HttpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
            error = $"Choose 1 to {ApproveSponsorshipChanges.MaximumRecords} records that change only sponsorships.";
        }
        await LoadAsync();
    }

    private ImmutableArray<ActionApprovalTarget> ParseTargets() => [.. (Targets ?? []).Select(ParseTarget)];

    private static ActionApprovalTarget ParseTarget(string value)
    {
        var parts = value.Split(':');
        if (parts.Length != 3 || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var revision) ||
            !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var stateVersion))
            throw new ArgumentException("A selected record is not valid.");
        return new(parts[0], revision, stateVersion);
    }

    private static string TargetOf(ActionReview item) =>
        string.Create(CultureInfo.InvariantCulture, $"{item.RecordId}:{item.CurrentRevision.RevisionNumber}:{item.ReviewStateVersion}");

    private static string OutcomeLabel(ActionApprovalOutcome outcome) => outcome switch
    {
        ActionApprovalOutcome.Approved => "Approved",
        ActionApprovalOutcome.Conflict => "Changed since the page loaded. Open it and check again.",
        ActionApprovalOutcome.NotFound => "Not found",
        _ => "Not approved. It has open concerns or changes more than sponsorships."
    };

    private IReadOnlyList<FilterTab> DraftTabs => [
        new("All", Href(null, null), Filter == DraftStatusFilter.All, counts!.NeedsAction + counts.Approved),
        new("Needs action", Href(DraftFilterNames.NeedsAction, null), Filter == DraftStatusFilter.NeedsAction, counts.NeedsAction),
        new("Approved", Href(DraftFilterNames.Approved, null), Filter == DraftStatusFilter.Approved, counts.Approved)
    ];

    private string? DraftsHref(string? cursor) => cursor is null ? null : Href(DraftFilterNames.NameOf(Filter), cursor);

    private static string Href(string? filter, string? cursor)
    {
        var query = new List<string>();
        if (filter is not null) query.Add("drafts=" + filter);
        if (cursor is not null) query.Add("draftsCursor=" + Uri.EscapeDataString(cursor));
        return ActionRoutes.List + (query.Count == 0 ? "" : "?" + string.Join('&', query)) + "#drafts";
    }

    private (string Title, string Text) DraftsEmpty => Filter switch
    {
        DraftStatusFilter.NeedsAction => ("No actions need action", "Every action record is approved, or none exists yet."),
        DraftStatusFilter.Approved => ("No approved actions", "Approved action records show here. Approval keeps them private."),
        _ => ("No action records yet", "The AI drafter writes one record for each tracked bill.")
    };
}
