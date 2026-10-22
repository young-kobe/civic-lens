using CivicLens.Host.Review;
using Microsoft.Extensions.Logging;
using CivicLens.Application.Review;
using CivicLens.Application.Statements;
using CivicLens.Host.Components.Layout;
using CivicLens.Host.Components.Ui;
using Microsoft.AspNetCore.Components;

namespace CivicLens.Host.Statements;

public sealed partial class StatementList
{
    private const int PageSize = 10;
    private const string StartForm = "start-statement";

    private static readonly IReadOnlyList<Crumb> Crumbs = [new("Review", "/Review"), new("Statements")];

    private readonly string renderKey = Guid.NewGuid().ToString("N");
    private StatementReviewPage? drafts;
    private StatementNotInReviewPage? notInReview;
    private StatementReviewCounts? counts;

    [Inject] private ListStatementReviews ListDrafts { get; set; } = default!;
    [Inject] private GetStatementReviewCounts GetCounts { get; set; } = default!;
    [Inject] private ListStatementsNotInReview ListNotInReview { get; set; } = default!;
    [Inject] private CreateStatementDraft CreateDraft { get; set; } = default!;
    [Inject] private NavigationManager Navigation { get; set; } = default!;

    [SupplyParameterFromQuery(Name = "notInReview")] private string? NotInReviewCursor { get; set; }
    [SupplyParameterFromQuery(Name = "status")] private string? Status { get; set; }

    [SupplyParameterFromForm(FormName = StartForm, Name = "candidateId")] private string? StartCandidateId { get; set; }
    [SupplyParameterFromForm(FormName = StartForm, Name = "idempotencyKey")] private string? StartKey { get; set; }

    private string? StatusMessage => Status switch
    {
        "invalid" => "Choose a statement that has no draft to start one.",
        "unavailable" => "The draft could not be started. Refresh the page and try again.",
        _ => null
    };

    protected override async Task OnInitializedAsync()
    {
        await ReadActorAsync();
        if (HttpMethods.IsPost(HttpContext.Request.Method)) return;
        await LoadGuardedAsync(async () =>
        {
            drafts = await ListDrafts.ExecuteAsync(actor, DraftsCursor, PageSize, ReadListFilter(), RequestAborted);
            counts = await GetCounts.ExecuteAsync(actor, RequestAborted);
            notInReview = await ListNotInReview.ExecuteAsync(actor, NotInReviewCursor, PageSize, RequestAborted);
        }, "The statement list could not load. Refresh the page to try again.");
    }

    private async Task StartDraftAsync() => Navigation.NavigateTo(await StartTargetAsync());

    private async Task<string> StartTargetAsync()
    {
        try
        {
            return await StatementDraftStart.ExecuteAsync(CreateDraft, actor, StartCandidateId, StartKey, RequestAborted);
        }
        catch (OperationCanceledException) when (RequestAborted.IsCancellationRequested) { throw; }
        catch (ArgumentException) { return StatementRoutes.List + "?status=invalid"; }
        catch (Exception exception)
        {
            Logger.LogError(exception, "A statement draft could not start.");
            return StatementRoutes.List + "?status=unavailable";
        }
    }

    private static string HeadlineOf(StatementReviewListItem draft) => draft.CurrentRevision is null
        ? DraftStatusDisplay.Label(draft.CurrentStatus, 0) : DraftStatusDisplay.DraftName(draft.CurrentRevision.Headline);

    private IReadOnlyList<FilterTab> DraftTabs =>
        DraftFilterTabs.For(Filter, name => StatementHref(name, null, NotInReviewCursor, "drafts"), counts!.NeedsAction, counts.Approved);

    private string? DraftsHref(string? cursor) =>
        cursor is null ? null : StatementHref(DraftFilterNames.NameOf(Filter), cursor, NotInReviewCursor, "drafts");

    private string? NotInReviewHref(string? cursor) =>
        cursor is null ? null : StatementHref(DraftFilterNames.NameOf(Filter), DraftsCursor, cursor, "not-in-review");

    private string StatementHref(string? status, string? draftsCursor, string? notInReviewCursor, string fragment) =>
        Href(StatementRoutes.List, status, draftsCursor, fragment, Part("notInReview", notInReviewCursor));

    private (string Title, string Text) DraftsEmpty => Filter switch
    {
        DraftStatusFilter.NeedsAction => ("No statements need action", "Every draft is approved, or no draft exists yet."),
        DraftStatusFilter.Approved => ("No approved statements", "Approved statements show here. Approval keeps them private."),
        _ => ("No statement drafts yet", "The AI drafter writes a draft for each article that passes the gate. You can also start one below.")
    };
}
