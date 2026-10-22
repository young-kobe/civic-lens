using CivicLens.Application.Review;
using CivicLens.Application.Votes;
using CivicLens.Host.Components.Layout;
using CivicLens.Host.Components.Ui;
using CivicLens.Host.Review;
using Microsoft.AspNetCore.Components;

namespace CivicLens.Host.Votes;

public sealed partial class VoteList
{
    private const int PageSize = 25;

    private static readonly IReadOnlyList<Crumb> Crumbs = [new("Review", "/Review"), new("Votes")];

    private VoteReviewPage? drafts;
    private VoteReviewCounts? counts;

    [Inject] private ListVoteReviews ListDrafts { get; set; } = default!;
    [Inject] private GetVoteReviewCounts GetCounts { get; set; } = default!;

    protected override async Task OnInitializedAsync()
    {
        await ReadActorAsync();
        await LoadGuardedAsync(async () =>
        {
            drafts = await ListDrafts.ExecuteAsync(actor, DraftsCursor, PageSize, ReadListFilter(), RequestAborted);
            counts = await GetCounts.ExecuteAsync(actor, RequestAborted);
        }, "The vote list could not load. Refresh the page to try again.");
    }

    private IReadOnlyList<FilterTab> DraftTabs =>
        DraftFilterTabs.For(Filter, name => Href(VoteRoutes.List, name, null, "drafts"), counts!.NeedsAction, counts.Approved);

    private string? DraftsHref(string? cursor) =>
        cursor is null ? null : Href(VoteRoutes.List, DraftFilterNames.NameOf(Filter), cursor, "drafts");

    private (string Title, string Text) DraftsEmpty => Filter switch
    {
        DraftStatusFilter.NeedsAction => ("No votes need action", "Every vote record is approved, or none exists yet."),
        DraftStatusFilter.Approved => ("No approved votes", "Approved vote records show here. Approval keeps them private."),
        _ => ("No vote records yet", "The AI drafter writes one record for each roll call that a tracked member voted in.")
    };
}
