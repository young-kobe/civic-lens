using CivicLens.Application.Documents;
using CivicLens.Core.Documents;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CivicLens.Host.Pages.Documents;

public sealed class ComparisonModel(IDocumentComparisonStore store) : PageModel
{
    public DocumentComparison? Comparison { get; private set; }
    public string? Error { get; private set; }
    public string ErrorTitle { get; private set; } = "Comparison unavailable";
    public bool IsMissing { get; private set; }

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
}
