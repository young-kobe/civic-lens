using System.Globalization;
using CivicLens.Application.Documents;
using CivicLens.Core.Documents;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CivicLens.Host.Pages.Documents;

public sealed class CitationModel(IDocumentExtractionStore store) : PageModel
{
    public DocumentTextSpan? Span { get; private set; }
    public string? Error { get; private set; }
    public string ErrorTitle { get; private set; } = "Citation unavailable";
    public bool IsMissing { get; private set; }
    public string? ExtractionId { get; private set; }

    public async Task<IActionResult> OnGetAsync(string? extractionId, string? start, string? length, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid || !ExtractionModel.IsHash(extractionId) ||
            !int.TryParse(start, NumberStyles.None, CultureInfo.InvariantCulture, out var offset) ||
            !int.TryParse(length, NumberStyles.None, CultureInfo.InvariantCulture, out var count) || offset < 0 || count <= 0) return BadRequest("Invalid evidence query. Check the source, exact URL, lowercase evidence ID, or citation offsets and return to the evidence lookup.");
        ExtractionId = extractionId;
        try { Span = await new GetDocumentCitation(store).ExecuteAsync(extractionId!, offset, count, cancellationToken); }
        catch (ArgumentException)
        {
            try
            {
                var extraction = await store.GetAsync(extractionId!, cancellationToken);
                if (extraction is null) { Response.StatusCode = StatusCodes.Status404NotFound; ErrorTitle = "Extraction not found"; Error = "No retained extraction has this ID."; IsMissing = true; }
                else { Response.StatusCode = StatusCodes.Status400BadRequest; ErrorTitle = "Citation span is invalid"; Error = "The requested range must fit inside the extraction and cannot split a Unicode character. Return to the extraction and choose a valid range."; }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception) { Response.StatusCode = StatusCodes.Status503ServiceUnavailable; Error = "The evidence store could not provide this citation. Try again later."; }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception) { Response.StatusCode = StatusCodes.Status503ServiceUnavailable; Error = "The evidence store could not provide this citation. Try again later."; }
        return Page();
    }
}
