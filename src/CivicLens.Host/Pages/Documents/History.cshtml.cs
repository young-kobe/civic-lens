using CivicLens.Application.Documents;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CivicLens.Host.Pages.Documents;

public sealed class HistoryModel(IDocumentHistoryStore store) : PageModel
{
    public const int Limit = GetDocumentHistory.MaximumObservations;
    public DocumentHistory? History { get; private set; }
    public string? Error { get; private set; }
    public string ErrorTitle { get; private set; } = "History unavailable";
    public string? SourceId { get; private set; }
    public string? RequestedUrl { get; private set; }

    public async Task<IActionResult> OnGetAsync(string? sourceId, string? requestedUrl, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid || sourceId is null || requestedUrl is null || string.IsNullOrWhiteSpace(sourceId) || string.IsNullOrWhiteSpace(requestedUrl)) return BadRequest("Invalid evidence query. Check the source, exact URL, lowercase evidence ID, or citation offsets and return to the evidence lookup.");
        SourceId = sourceId; RequestedUrl = requestedUrl;
        try { History = await new GetDocumentHistory(store).ExecuteAsync(sourceId, requestedUrl, Limit, cancellationToken); }
        catch (DocumentHistoryLimitException) { Error = "This history exceeds the safe display limit. It has not been partially loaded."; ErrorTitle = "History exceeds display limits"; }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception) { Response.StatusCode = StatusCodes.Status503ServiceUnavailable; Error = "The evidence store could not provide this history. Try again later."; }
        return Page();
    }
}
