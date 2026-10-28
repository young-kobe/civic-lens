namespace CivicLens.Application.Collection.MemberPhotos;

public static class MemberPhotoUrl
{
    private const string Extension = ".jpg";

    public static string Photo(string sourceUrl, string bioguideId) => $"{Prefix(sourceUrl)}{bioguideId}{Extension}";

    public static string? TryReadBioguideId(string sourceUrl, string url)
    {
        var prefix = Prefix(sourceUrl);
        if (!url.StartsWith(prefix, StringComparison.Ordinal) || !url.EndsWith(Extension, StringComparison.Ordinal))
            return null;
        var bioguideId = url[prefix.Length..^Extension.Length];
        return BioguideId.IsValid(bioguideId) ? bioguideId : null;
    }

    public static string ReadBioguideId(string sourceUrl, string url) =>
        TryReadBioguideId(sourceUrl, url) ?? throw new InvalidDataException("Job URL is not a member photo URL.");

    private static string Prefix(string sourceUrl) => sourceUrl.TrimEnd('/') + "/";
}
