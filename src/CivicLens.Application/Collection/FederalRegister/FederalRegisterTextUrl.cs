namespace CivicLens.Application.Collection.FederalRegister;

/// <summary>Reads the URL of a Federal Register text job. The URL is the <c>body_html_url</c> that the detail response pinned, so the
/// API owns its path. The reader checks only that the canonical URL starts with the source URL and ends in the document number.</summary>
public static class FederalRegisterTextUrl
{
    private const string Suffix = ".html";

    public static string? TryReadNumber(string sourceUrl, string url)
    {
        if (!url.StartsWith(sourceUrl, StringComparison.Ordinal) || !url.EndsWith(Suffix, StringComparison.Ordinal) ||
            !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.AbsoluteUri != url || uri.Query.Length > 0 || uri.Fragment.Length > 0)
            return null;
        var number = url[(url.LastIndexOf('/') + 1)..^Suffix.Length];
        return FederalRegisterDocumentNumber.IsValid(number) ? number : null;
    }

    public static string ReadNumber(string sourceUrl, string url) =>
        TryReadNumber(sourceUrl, url) ?? throw new InvalidDataException("Job URL is not a Federal Register text URL.");
}
