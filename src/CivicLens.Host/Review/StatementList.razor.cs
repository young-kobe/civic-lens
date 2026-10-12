using CivicLens.Application.Review;
using CivicLens.Application.Statements;
using CivicLens.Core.Review;
using CivicLens.Host.Components.Layout;
using CivicLens.Host.Components.Ui;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;

namespace CivicLens.Host.Review;

public sealed partial class StatementList
{
    private const int PageSize = 10;
    private const string StartForm = "start-statement";

    private static readonly IReadOnlyList<Crumb> Crumbs = [new("Review", "/Review"), new("Statements")];

    private readonly string renderKey = Guid.NewGuid().ToString("N");
    private ReviewActor? actor;
    private StatementReviewPage? drafts;
    private StatementNotInReviewPage? notInReview;
    private string? error;

    [Inject] private ReviewActorAccessor Actors { get; set; } = default!;
    [Inject] private ListStatementReviews ListDrafts { get; set; } = default!;
    [Inject] private ListStatementsNotInReview ListNotInReview { get; set; } = default!;
    [Inject] private CreateStatementDraft CreateDraft { get; set; } = default!;
    [Inject] private NavigationManager Navigation { get; set; } = default!;

    [CascadingParameter] private HttpContext HttpContext { get; set; } = default!;
    [CascadingParameter] private Task<AuthenticationState> AuthenticationState { get; set; } = default!;

    [SupplyParameterFromQuery(Name = "drafts")] private string? DraftsFilterName { get; set; }
    [SupplyParameterFromQuery(Name = "draftsCursor")] private string? DraftsCursor { get; set; }
    [SupplyParameterFromQuery(Name = "notInReview")] private string? NotInReviewCursor { get; set; }
    [SupplyParameterFromQuery(Name = "status")] private string? Status { get; set; }

    [SupplyParameterFromForm(FormName = StartForm, Name = "candidateId")] private string? StartCandidateId { get; set; }
    [SupplyParameterFromForm(FormName = StartForm, Name = "idempotencyKey")] private string? StartKey { get; set; }

    private DraftStatusFilter Filter => DraftFilterNames.Parse(DraftsFilterName);

    private string? StatusMessage => Status switch
    {
        "invalid" => "Choose a statement that has no draft to start one.",
        "unavailable" => "The draft could not be started. Refresh the page and try again.",
        _ => null
    };

    protected override async Task OnInitializedAsync()
    {
        actor = Actors.GetActor((await AuthenticationState).User);
        if (HttpMethods.IsPost(HttpContext.Request.Method)) return;
        var cancellationToken = HttpContext.RequestAborted;
        try
        {
            drafts = await ListDrafts.ExecuteAsync(actor, DraftsCursor, PageSize, Filter, cancellationToken);
            notInReview = await ListNotInReview.ExecuteAsync(actor, NotInReviewCursor, PageSize, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (ArgumentException)
        {
            error = "This page link is no longer valid. Go back to the first page of the list.";
        }
        catch (Exception)
        {
            error = "The statement list could not load. Refresh the page to try again.";
        }
    }

    private async Task StartDraftAsync() => Navigation.NavigateTo(await StartTargetAsync());

    private async Task<string> StartTargetAsync()
    {
        var cancellationToken = HttpContext.RequestAborted;
        try
        {
            return await StatementDraftStart.ExecuteAsync(CreateDraft, actor!, StartCandidateId, StartKey, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (ArgumentException) { return StatementRoutes.List + "?status=invalid"; }
        catch (Exception) { return StatementRoutes.List + "?status=unavailable"; }
    }

    private static string HeadlineOf(StatementReviewListItem draft) => draft.CurrentRevision is null
        ? DraftStatusDisplay.Label(draft.CurrentStatus, 0) : DraftStatusDisplay.DraftName(draft.CurrentRevision.Headline);

    private IReadOnlyList<FilterTab> DraftTabs => [
        new("All", Href(null, null, NotInReviewCursor, "drafts"), Filter == DraftStatusFilter.All),
        new("Needs action", Href(DraftFilterNames.NeedsAction, null, NotInReviewCursor, "drafts"), Filter == DraftStatusFilter.NeedsAction),
        new("Approved", Href(DraftFilterNames.Approved, null, NotInReviewCursor, "drafts"), Filter == DraftStatusFilter.Approved)
    ];

    private string? DraftsHref(string? cursor) =>
        cursor is null ? null : Href(DraftFilterNames.NameOf(Filter), cursor, NotInReviewCursor, "drafts");

    private string? NotInReviewHref(string? cursor) =>
        cursor is null ? null : Href(DraftFilterNames.NameOf(Filter), DraftsCursor, cursor, "not-in-review");

    private static string Href(string? filter, string? draftsCursor, string? notInReviewCursor, string fragment)
    {
        var query = new List<string>();
        if (filter is not null) query.Add("drafts=" + filter);
        if (draftsCursor is not null) query.Add("draftsCursor=" + Uri.EscapeDataString(draftsCursor));
        if (notInReviewCursor is not null) query.Add("notInReview=" + Uri.EscapeDataString(notInReviewCursor));
        return StatementRoutes.List + (query.Count == 0 ? "" : "?" + string.Join('&', query)) + "#" + fragment;
    }

    private (string Title, string Text) DraftsEmpty => Filter switch
    {
        DraftStatusFilter.NeedsAction => ("No statements need action", "Every draft is approved, or no draft exists yet."),
        DraftStatusFilter.Approved => ("No approved statements", "Approved statements show here. Approval keeps them private."),
        _ => ("No statement drafts yet", "The AI drafter writes a draft for each article that passes the gate. You can also start one below.")
    };
}
