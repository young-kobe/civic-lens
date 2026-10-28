namespace CivicLens.Application.Publication;

public sealed class PublicBaseUrl
{
    private readonly string origin;

    private PublicBaseUrl(string origin) => this.origin = origin;

    public static PublicBaseUrl? Parse(string? value, string variableName)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var text = value.Trim();
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || !IsBareOrigin(uri) || !IsAllowedScheme(uri))
            throw new ArgumentException(
                $"{variableName} must be a bare origin such as https://example.org, with no user, path, query, or fragment. " +
                "Only localhost and 127.0.0.1 may use http.");
        return new(text.TrimEnd('/'));
    }

    public string Join(string releasePath) => $"{origin}/{releasePath}";

    private static bool IsBareOrigin(Uri uri) =>
        uri.Host.Length > 0 && uri.UserInfo.Length == 0 && uri.Query.Length == 0 && uri.Fragment.Length == 0 && uri.AbsolutePath == "/"
        && !uri.OriginalString.Contains('?') && !uri.OriginalString.Contains('#');

    private static bool IsAllowedScheme(Uri uri) =>
        uri.Scheme == Uri.UriSchemeHttps || (uri.Scheme == Uri.UriSchemeHttp && uri.Host is "localhost" or "127.0.0.1");
}
