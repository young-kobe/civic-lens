namespace CivicLens.Publication.Contracts;

public static class PublishedWebsite
{
    public const int MaximumLength = 512;

    public static bool IsValid(string? value) =>
        value is { Length: > 0 and <= MaximumLength } &&
        value.StartsWith("https://", StringComparison.Ordinal) && !value.Any(char.IsWhiteSpace) &&
        Uri.TryCreate(value, UriKind.Absolute, out var url) && url.UserInfo.Length == 0;
}
