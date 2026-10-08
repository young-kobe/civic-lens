using CivicLens.Application.Documents;
using CivicLens.Application.Review;
using CivicLens.Core.Documents;
using CivicLens.Host.Review;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CivicLens.Host.Pages.Documents;

public sealed class ComparisonModel(IDocumentComparisonStore store,
    CreateDocumentChangeDraft? createDraft = null, ReviewActorAccessor? actorAccessor = null) : PageModel
{
    public DocumentComparison? Comparison { get; private set; }
    public string? Error { get; private set; }
    public string ErrorTitle { get; private set; } = "Comparison unavailable";
    public bool IsMissing { get; private set; }
    public bool CanCreateReview => createDraft is not null && actorAccessor?.IsAuthorized(User) == true;
    public string CreateIdempotencyKey { get; private set; } = Guid.NewGuid().ToString("N");
    public string? ReviewStatus { get; private set; }

    public async Task<IActionResult> OnGetAsync(string? comparisonId, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid || !ExtractionModel.IsHash(comparisonId)) return BadRequest("Invalid evidence query. Check the source, exact URL, lowercase evidence ID, or citation offsets and return to the evidence lookup.");
        try
        {
            Comparison = await new GetDocumentComparison(store).ExecuteAsync(comparisonId!, cancellationToken);
            if (Comparison is null) { Response.StatusCode = StatusCodes.Status404NotFound; ErrorTitle = "Comparison not found"; Error = "No saved comparison has this ID."; IsMissing = true; }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception) { Response.StatusCode = StatusCodes.Status503ServiceUnavailable; Error = "The evidence store could not provide this comparison. Try again later."; }
        return Page();
    }

    public async Task<IActionResult> OnPostCreateAsync(string? comparisonId, string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid || !ExtractionModel.IsHash(comparisonId) ||
            string.IsNullOrWhiteSpace(idempotencyKey) || createDraft is null || actorAccessor is null)
            return BadRequest("The review request is invalid. Return to the saved comparison and try again.");
        try
        {
            CreateIdempotencyKey = idempotencyKey;
            var actor = actorAccessor.GetActor(User);
            var revision = await createDraft.ExecuteAsync(actor,
                new CreateDocumentChangeDraftRequest(comparisonId!, idempotencyKey), cancellationToken);
            return RedirectToPage("/Review/Editor", new { draftId = revision.DraftId });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (ArgumentException)
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
            ReviewStatus = "This comparison cannot start a review. Return to the review queue to choose an eligible saved comparison.";
            return await OnGetAsync(comparisonId, cancellationToken);
        }
        catch (Exception)
        {
            Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            ReviewStatus = "The review draft could not be created. Try again from this comparison.";
            return await OnGetAsync(comparisonId, cancellationToken);
        }
    }
}
