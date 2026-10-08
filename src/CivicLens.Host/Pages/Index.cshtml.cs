using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CivicLens.Host.Pages;

public sealed class IndexModel : PageModel
{
    public string? Error { get; private set; }

    public void OnGet(string? error)
    {
        if (error == "invalid") Error = "The link contains invalid input. Check the identifier, URL, or citation offsets and try again.";
        else if (error == "unavailable") Error = "The evidence store is temporarily unavailable. Try again later.";
    }
}
