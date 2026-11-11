using System.Globalization;

namespace CivicLens.Application.Collection.FederalRegister;

/// <summary>Owns the one request form of the Federal Register text source: the HTML body of one document.
/// The path holds the publication date and the document number, like the <c>body_html_url</c> of the API.</summary>
public static class FederalRegisterTextUrl
{
    private const string Suffix = ".html";

    public static string Body(string sourceUrl, DateOnly publishedOn, string documentNumber) =>
        $"{sourceUrl}/{publishedOn.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture)}/{documentNumber}{Suffix}";

    public static (string DocumentNumber, DateOnly PublishedOn)? TryRead(string sourceUrl, string url)
    {
        var prefix = sourceUrl + "/";
        if (!url.StartsWith(prefix, StringComparison.Ordinal) || !url.EndsWith(Suffix, StringComparison.Ordinal)) return null;
        var parts = url[prefix.Length..^Suffix.Length].Split('/');
        if (parts.Length != 4 || !FederalRegisterDocumentNumber.IsValid(parts[3]) ||
            !DateOnly.TryParseExact(string.Join('-', parts[..3]), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var publishedOn))
            return null;
        return Body(sourceUrl, publishedOn, parts[3]) == url ? (parts[3], publishedOn) : null;
    }

    public static string? TryReadNumber(string sourceUrl, string url) => TryRead(sourceUrl, url)?.DocumentNumber;

    public static (string DocumentNumber, DateOnly PublishedOn) Read(string sourceUrl, string url) =>
        TryRead(sourceUrl, url) ?? throw new InvalidDataException("Job URL is not a Federal Register text URL.");
}
