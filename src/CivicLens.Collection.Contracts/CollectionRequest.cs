using System.Text.Json;
using System.Text.Json.Serialization;

namespace CivicLens.Collection.Contracts;

public sealed record CollectionRequest
{
    [JsonRequired]
    public int Version { get; init; } = CollectionProtocol.Version;
    public required string JobId { get; init; }
    public required string SourceId { get; init; }
    public required string Url { get; init; }
    public required string AllowedOrigin { get; init; }
    public required string AllowedPathPrefix { get; init; }
    public required string ArtifactDirectory { get; init; }
    public CollectionMode Mode { get; init; } = CollectionMode.Page;
    public int MaxCandidates { get; init; } = 100;
    public int MaxRequests { get; init; } = 5;
    public long MaxBytes { get; init; } = 2_000_000;
    public int TimeoutSeconds { get; init; } = 30;
    public int MinDelayMilliseconds { get; init; } = 1000;
    public string? ETag { get; init; }
    public DateTimeOffset? LastModified { get; init; }

    public void Validate()
    {
        ValidateIdentity();
        ValidateOrigin();
        ValidatePathPrefix();
        ValidateRequestedUrl();
        ValidateArtifactDirectory();
        ValidateBudgets();
        ValidateETag();
        if (JsonSerializer.SerializeToUtf8Bytes(this, CollectionProtocol.JsonOptions).Length > CollectionProtocol.MaximumManifestBytes)
            throw new ArgumentException("Serialized collection manifest exceeds 64 KiB.");
    }

    private void ValidateIdentity()
    {
        if (Version is not (CollectionProtocol.Version or CollectionProtocol.PreviousVersion or CollectionProtocol.LegacyPageVersion))
            throw new ArgumentException("Unsupported collection protocol version.");
        if (!Enum.IsDefined(Mode) || (Version == CollectionProtocol.LegacyPageVersion &&
            (Mode != CollectionMode.Page || MaxCandidates != 100)) ||
            (Mode == CollectionMode.Html && Version != CollectionProtocol.Version))
            throw new ArgumentException("Collection mode is unsupported for this protocol version.");
        if (string.IsNullOrWhiteSpace(JobId) || string.IsNullOrWhiteSpace(SourceId))
            throw new ArgumentException("Job and source IDs are required.");
    }

    private void ValidateOrigin()
    {
        if (!Uri.TryCreate(AllowedOrigin, UriKind.Absolute, out var origin) ||
            origin.Scheme is not ("https" or "http") || origin.UserInfo.Length != 0 ||
            origin.AbsolutePath != "/" || origin.Query.Length != 0 || origin.Fragment.Length != 0)
            throw new ArgumentException("Allowed origin must be an HTTP(S) origin without credentials, path, query, or fragment.");
    }

    private void ValidatePathPrefix()
    {
        if (string.IsNullOrEmpty(AllowedPathPrefix) || !AllowedPathPrefix.StartsWith('/') ||
            AllowedPathPrefix.Contains('?') || AllowedPathPrefix.Contains('#') || AllowedPathPrefix.Contains('%') ||
            AllowedPathPrefix.Contains('\\') || AllowedPathPrefix.Split('/').Any(s => s is "." or ".."))
            throw new ArgumentException("Allowed path prefix must be a plain absolute path.");
    }

    private void ValidateRequestedUrl()
    {
        if (!Uri.TryCreate(Url, UriKind.Absolute, out var url) || !Allows(url))
            throw new ArgumentException("Requested URL is outside its allowed scope or contains credentials or a fragment.");
    }

    private void ValidateArtifactDirectory()
    {
        if (string.IsNullOrWhiteSpace(ArtifactDirectory) || !Path.IsPathFullyQualified(ArtifactDirectory))
            throw new ArgumentException("Artifact directory must be an absolute path.");
    }

    private void ValidateBudgets()
    {
        if (MaxRequests is < 2 or > 20 || MaxBytes is < 1 or > 100_000_000 ||
            TimeoutSeconds is < 1 or > 300 || MinDelayMilliseconds is < 0 or > 60_000 || MaxCandidates is < 1 or > 1000)
            throw new ArgumentException("Collection budgets are outside supported bounds.");
    }

    private void ValidateETag()
    {
        if (ETag is not null && (!System.Net.Http.Headers.EntityTagHeaderValue.TryParse(ETag, out var tag) ||
            tag.Tag == "*" || ETag.Length > 4096))
            throw new ArgumentException("ETag must be a single entity tag.");
    }

    public bool Allows(Uri url)
    {
        if (url is null || !Uri.TryCreate(AllowedOrigin, UriKind.Absolute, out var origin) ||
            !url.IsAbsoluteUri || url.Scheme != origin.Scheme || url.IdnHost != origin.IdnHost ||
            url.Port != origin.Port || url.UserInfo.Length != 0 || url.Fragment.Length != 0)
            return false;
        var path = Uri.UnescapeDataString(url.AbsolutePath);
        if (path.Contains('\\') || path.Contains('%') || path.Split('/').Any(s => s is "." or ".."))
            return false;
        return path == AllowedPathPrefix || path.StartsWith(
            AllowedPathPrefix.EndsWith('/') ? AllowedPathPrefix : AllowedPathPrefix + "/", StringComparison.Ordinal);
    }
}
