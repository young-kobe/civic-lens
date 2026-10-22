using CivicLens.Application.Actions;
using CivicLens.Application.Review;
using CivicLens.Host.Components.Layout;
using CivicLens.Host.Components.Ui;
using CivicLens.Host.Review;
using Microsoft.AspNetCore.Components;

namespace CivicLens.Host.Actions;

public sealed partial class ActionList
{
    private const int PageSize = 25;

    private static readonly IReadOnlyList<Crumb> Crumbs = [new("Review", "/Review"), new("Actions")];

    private ActionReviewPage? drafts;
    private ActionReviewCounts? counts;

    [Inject] private ListActionReviews ListDrafts { get; set; } = default!;
    [Inject] private GetActionReviewCounts GetCounts { get; set; } = default!;

    protected override async Task OnInitializedAsync()
    {
        await ReadActorAsync();
        await LoadGuardedAsync(async () =>
        {
            drafts = await ListDrafts.ExecuteAsync(actor, DraftsCursor, PageSize, ReadListFilter(), RequestAborted);
            counts = await GetCounts.ExecuteAsync(actor, RequestAborted);
        }, "The action list could not load. Refresh the page to try again.");
    }

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
