using CivicLens.Application.Relationships;
using CivicLens.Application.Review;
using CivicLens.Core.Review;
using CivicLens.Host.Components.Layout;
using CivicLens.Host.Components.Ui;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;

namespace CivicLens.Host.Review;

public sealed partial class RelationshipList
{
    private const int PageSize = 25;

    private static readonly IReadOnlyList<Crumb> Crumbs = [new("Review", "/Review"), new("Relationships")];

    private ReviewActor? actor;
    private RelationshipReviewPage? drafts;
    private RelationshipReviewCounts? counts;
    private string? error;

    [Inject] private ReviewActorAccessor Actors { get; set; } = default!;
    [Inject] private ListRelationshipReviews ListDrafts { get; set; } = default!;
    [Inject] private GetRelationshipReviewCounts GetCounts { get; set; } = default!;

    [CascadingParameter] private HttpContext HttpContext { get; set; } = default!;
    [CascadingParameter] private Task<AuthenticationState> AuthenticationState { get; set; } = default!;

    [SupplyParameterFromQuery(Name = "drafts")] private string? DraftsFilterName { get; set; }
    [SupplyParameterFromQuery(Name = "draftsCursor")] private string? DraftsCursor { get; set; }

    private DraftStatusFilter Filter => DraftFilterNames.Parse(DraftsFilterName);

    protected override async Task OnInitializedAsync()
    {
        actor = Actors.GetActor((await AuthenticationState).User);
        var cancellationToken = HttpContext.RequestAborted;
        try
        {
            drafts = await ListDrafts.ExecuteAsync(actor, DraftsCursor, PageSize, Filter, cancellationToken);
            counts = await GetCounts.ExecuteAsync(actor, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (ArgumentException)
        {
            error = "This page link is no longer valid. Go back to the first page of the list.";
        }
        catch (Exception)
        {
            error = "The relationship list could not load. Refresh the page to try again.";
        }
    }

    private IReadOnlyList<FilterTab> DraftTabs =>
        DraftFilterTabs.For(Filter, name => Href(name, null), counts!.NeedsAction, counts.Approved);

    private string? DraftsHref(string? cursor) => cursor is null ? null : Href(DraftFilterNames.NameOf(Filter), cursor);

    private static string Href(string? filter, string? cursor)
    {
        var query = new List<string>();
        if (filter is not null) query.Add("drafts=" + filter);
        if (cursor is not null) query.Add("draftsCursor=" + Uri.EscapeDataString(cursor));
        return RelationshipRoutes.List + (query.Count == 0 ? "" : "?" + string.Join('&', query)) + "#drafts";
    }

    private (string Title, string Text) DraftsEmpty => Filter switch
    {
        DraftStatusFilter.NeedsAction => ("No relationships need action", "Every relationship is approved, or none exists yet."),
        DraftStatusFilter.Approved => ("No approved relationships", "Approved relationships show here. Approval keeps them private."),
        _ => ("No relationships yet", "The AI drafter proposes a link when an approved statement and an approved action or vote share a bill.")
    };
}
