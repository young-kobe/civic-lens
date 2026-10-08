using System.Text.RegularExpressions;
using CivicLens.Application.Documents;
using CivicLens.Core.Documents;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CivicLens.Host.Pages.Documents;

public sealed partial class ExtractionModel(IDocumentExtractionStore store) : PageModel
{
    public DocumentExtraction? Extraction { get; private set; }
    public string? Error { get; private set; }
    public string ErrorTitle { get; private set; } = "Evidence unavailable";
    public bool IsMissing { get; private set; }

    public async Task<IActionResult> OnGetAsync(string? extractionId, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid || !IsHash(extractionId)) return BadRequest("Invalid evidence query. Check the source, exact URL, lowercase evidence ID, or citation offsets and return to the evidence lookup.");
        try
        {
            Extraction = await store.GetAsync(extractionId!, cancellationToken);
            if (Extraction is null) { Response.StatusCode = StatusCodes.Status404NotFound; ErrorTitle = "Extraction not found"; Error = "No retained extraction has this ID."; IsMissing = true; }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception) { Response.StatusCode = StatusCodes.Status503ServiceUnavailable; Error = "The evidence store could not provide this extraction. Try again later."; }
        return Page();
    }

    internal static bool IsHash(string? value) => value is not null && HashPattern().IsMatch(value);

    [GeneratedRegex("\\A[0-9a-f]{64}\\z", RegexOptions.CultureInvariant)]
    private static partial Regex HashPattern();
}
