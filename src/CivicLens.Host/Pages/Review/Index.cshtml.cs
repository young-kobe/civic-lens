using CivicLens.Application.Review;
using CivicLens.Host.Review;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CivicLens.Host.Pages.Review;

[Authorize]
public sealed class IndexModel(ListEligibleDocumentComparisons listComparisons,
    ListDocumentChangeReviews listReviews, CreateDocumentChangeDraft createDraft,
    ReviewActorAccessor actorAccessor) : PageModel
{
    private const int PageSize = 20;

    public EligibleDocumentComparisonPage? Comparisons { get; private set; }
    public DocumentChangeReviewPage? Reviews { get; private set; }
    public string? Error { get; private set; }
    public string? Status { get; private set; }
    public bool IsError { get; private set; }
    public Dictionary<string, string> IdempotencyKeys { get; } = new(StringComparer.Ordinal);

    public async Task OnGetAsync(string? comparisonCursor, string? reviewCursor, string? status,
        CancellationToken cancellationToken)
    {
        if (status == "created") Status = "Draft created. Its first revision is ready to edit.";
        else if (status == "invalid") { Status = "Choose a saved comparison to create a draft."; IsError = true; }
        else if (status == "unavailable") { Status = "The draft could not be created. Refresh and try again."; IsError = true; }
        try
        {
            var actor = actorAccessor.GetActor(User);
            Comparisons = await listComparisons.ExecuteAsync(actor, comparisonCursor, PageSize, cancellationToken: cancellationToken);
            Reviews = await listReviews.ExecuteAsync(actor, reviewCursor, PageSize, cancellationToken: cancellationToken);
            foreach (var item in Comparisons.Items)
                IdempotencyKeys[item.Comparison.ComparisonId] = Guid.NewGuid().ToString("N");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (UnauthorizedAccessException) { Error = "Your signed-in account is not authorized for editorial review."; }
        catch (Exception)
        {
            Error = "The review workspace could not load its records. Try refreshing the page.";
        }
    }

    public async Task<IActionResult> OnPostCreateAsync(string? comparisonId, string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        try
        {
            var actor = actorAccessor.GetActor(User);
            if (string.IsNullOrWhiteSpace(comparisonId) || string.IsNullOrWhiteSpace(idempotencyKey))
                throw new ArgumentException("Choose a saved comparison to create a draft.");
            var revision = await createDraft.ExecuteAsync(actor,
                new CreateDocumentChangeDraftRequest(comparisonId, idempotencyKey), cancellationToken);
            return RedirectToPage("/Review/Editor", new { draftId = revision.DraftId });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (ArgumentException)
        {
            return RedirectToPage("/Review/Index", new { status = "invalid" });
        }
        catch (Exception)
        {
            return RedirectToPage("/Review/Index", new { status = "unavailable" });
        }
    }
}
