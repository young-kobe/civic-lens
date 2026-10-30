using System.Web;

namespace CivicLens.Application.Collection.FederalRegister;

/// <summary>Owns the two request forms of the Federal Register API: the list of a publication date window and one document.</summary>
public static class FederalRegisterUrl
{
    private const string JsonSuffix = ".json";
    private const string GreaterOrEqual = "conditions[publication_date][gte]";
    private const string LessOrEqual = "conditions[publication_date][lte]";
    public const int PageLimit = 1000;

    private static readonly string[] DocumentTypes = ["executive_order", "proclamation", "notice", "memorandum"];
    private static readonly string[] DetailFields =
    [
        "document_number", "type", "subtype", "title", "signing_date", "publication_date", "citation", "president", "correction_of",
        "presidential_document_number", "html_url"
    ];

    public static string List(string sourceUrl, FederalRegisterWindow window)
    {
        var parts = new List<string> { Pair("conditions[type][]", "PRESDOCU") };
        parts.AddRange(DocumentTypes.Select(type => Pair("conditions[presidential_document_type][]", type)));
        parts.Add(Pair(GreaterOrEqual, window.First.ToString("yyyy-MM-dd")));
        parts.Add(Pair(LessOrEqual, window.Last.ToString("yyyy-MM-dd")));
        parts.Add(Pair("fields[]", "document_number"));
        parts.Add(Pair("fields[]", "publication_date"));
        parts.Add(Pair("order", "oldest"));
        parts.Add(Pair("per_page", PageLimit.ToString()));
        return $"{sourceUrl}?{string.Join('&', parts)}";
    }

    public static string Document(string sourceUrl, string documentNumber) =>
        $"{DocumentBase(sourceUrl)}/{documentNumber}{JsonSuffix}?{string.Join('&', DetailFields.Select(field => Pair("fields[]", field)))}";

    public static FederalRegisterWindow? TryReadList(string sourceUrl, string url)
    {
        if (!url.StartsWith(sourceUrl + "?", StringComparison.Ordinal)) return null;
        var query = HttpUtility.ParseQueryString(new Uri(url).Query);
        if (!DateOnly.TryParseExact(query[GreaterOrEqual], "yyyy-MM-dd", out var first) ||
            !DateOnly.TryParseExact(query[LessOrEqual], "yyyy-MM-dd", out var last) || first > last)
            return null;
        var window = new FederalRegisterWindow(first, last);
        return List(sourceUrl, window) == url ? window : null;
    }

    public static string? TryReadDocument(string sourceUrl, string url)
    {
        var stem = DocumentBase(sourceUrl) + "/";
        if (!url.StartsWith(stem, StringComparison.Ordinal)) return null;
        var rest = url[stem.Length..];
        var end = rest.IndexOf(JsonSuffix + "?", StringComparison.Ordinal);
        if (end < 0) return null;
        var number = rest[..end];
        return FederalRegisterDocumentNumber.IsValid(number) && Document(sourceUrl, number) == url ? number : null;
    }

    public static FederalRegisterWindow ReadList(string sourceUrl, string url) =>
        TryReadList(sourceUrl, url) ?? throw new InvalidDataException("Job URL is not a Federal Register list URL.");

    public static string ReadDocument(string sourceUrl, string url) =>
        TryReadDocument(sourceUrl, url) ?? throw new InvalidDataException("Job URL is not a Federal Register document URL.");

    private static string DocumentBase(string sourceUrl) =>
        sourceUrl.EndsWith(JsonSuffix, StringComparison.Ordinal)
            ? sourceUrl[..^JsonSuffix.Length]
            : throw new ArgumentException("The Federal Register source URL ends with .json.", nameof(sourceUrl));

    private static string Pair(string key, string value) =>
        $"{key.Replace("[", "%5B").Replace("]", "%5D")}={Uri.EscapeDataString(value)}";
}
