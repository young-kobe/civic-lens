namespace CivicLens.Publication.Contracts;

public static class PublishedWebsite
{
    public const int MaximumLength = 512;

    public static bool IsValid(string? value) =>
        value is { Length: > 0 and <= MaximumLength } &&
        Uri.TryCreate(value, UriKind.Absolute, out var url) && url.Scheme == Uri.UriSchemeHttps && url.UserInfo.Length == 0;
}
