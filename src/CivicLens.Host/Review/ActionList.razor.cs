using System.Collections.Immutable;
using System.Globalization;
using CivicLens.Application.Actions;
using CivicLens.Application.Review;
using CivicLens.Core.Actions;
using CivicLens.Host.Components.Layout;
using CivicLens.Host.Components.Ui;
using Microsoft.AspNetCore.Components;

namespace CivicLens.Host.Review;

public sealed partial class ActionList
{
    private const int PageSize = 25;
    private const string ApproveForm = "approve-sponsorships";

    private static readonly IReadOnlyList<Crumb> Crumbs = [new("Review", "/Review"), new("Actions")];

    private readonly string renderKey = Guid.NewGuid().ToString("N");
    private ActionReviewPage? drafts;
    private ActionReviewCounts? counts;
    private IReadOnlyList<BulkApprovalResult>? approvals;

    [Inject] private ListActionReviews ListDrafts { get; set; } = default!;
    [Inject] private GetActionReviewCounts GetCounts { get; set; } = default!;
    [Inject] private ApproveSponsorshipChanges Approve { get; set; } = default!;

    [SupplyParameterFromForm(FormName = ApproveForm, Name = "targets")] private string[]? Targets { get; set; }
    [SupplyParameterFromForm(FormName = ApproveForm, Name = "idempotencyKey")] private string? ApproveKey { get; set; }

    protected override async Task OnInitializedAsync()
    {
        await ReadActorAsync();
        if (!HttpMethods.IsPost(HttpContext.Request.Method)) await LoadAsync();
    }

    private Task LoadAsync() => LoadGuardedAsync(async () =>
    {
        drafts = await ListDrafts.ExecuteAsync(actor, DraftsCursor, PageSize, ReadListFilter(), RequestAborted);
        counts = await GetCounts.ExecuteAsync(actor, RequestAborted);
    }, "The action list could not load. Refresh the page to try again.");

    private async Task ApproveAsync()
    {
        try
        {
            approvals = await Approve.ExecuteAsync(actor, new BulkApprovalRequest(ParseTargets(), ApproveKey ?? ""),
                RequestAborted);
        }
        catch (OperationCanceledException) when (RequestAborted.IsCancellationRequested) { throw; }
        catch (ArgumentException)
        {
            HttpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
            error = $"Choose 1 to {BulkApproval.MaximumRecords} records that change only sponsorships.";
        }
        await LoadAsync();
    }

    private ImmutableArray<BulkApprovalTarget> ParseTargets() => [.. (Targets ?? []).Select(ParseTarget)];

    private static BulkApprovalTarget ParseTarget(string value)
    {
        var parts = value.Split(':');
        if (parts.Length != 3 || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var revision) ||
            !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var stateVersion))
            throw new ArgumentException("A selected record is not valid.");
        return new(parts[0], revision, stateVersion);
    }

    private static string TargetOf(ActionReview item) =>
        string.Create(CultureInfo.InvariantCulture, $"{item.RecordId}:{item.CurrentRevision.RevisionNumber}:{item.ReviewStateVersion}");

    private static string OutcomeLabel(BulkApprovalOutcome outcome) => outcome switch
    {
        BulkApprovalOutcome.Approved => "Approved",
        BulkApprovalOutcome.Conflict => "Changed since the page loaded. Open it and check again.",
        BulkApprovalOutcome.NotFound => "Not found",
        _ => "Not approved. It has open concerns or changes more than sponsorships."
    };

    private IReadOnlyList<FilterTab> DraftTabs =>
        DraftFilterTabs.For(Filter, name => Href(ActionRoutes.List, name, null, "drafts"), counts!.NeedsAction, counts.Approved);

    private string? DraftsHref(string? cursor) =>
        cursor is null ? null : Href(ActionRoutes.List, DraftFilterNames.NameOf(Filter), cursor, "drafts");

    private (string Title, string Text) DraftsEmpty => Filter switch
    {
        DraftStatusFilter.NeedsAction => ("No actions need action", "Every action record is approved, or none exists yet."),
        DraftStatusFilter.Approved => ("No approved actions", "Approved action records show here. Approval keeps them private."),
        _ => ("No action records yet", "The AI drafter writes one record for each tracked bill.")
    };
}
