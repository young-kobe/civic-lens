using System.Text.Json;
using System.Text.Json.Serialization;

namespace CivicLens.Collection.Contracts;

public static class CollectionProtocol
{
    public const int Version = 1;

    static CollectionProtocol() => JsonOptions.MakeReadOnly(populateMissingResolver: true);
    public static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web)
    {
        AllowDuplicateProperties = false,
        NumberHandling = JsonNumberHandling.Strict,
        RespectNullableAnnotations = true,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter<CollectionOutcome>(JsonNamingPolicy.CamelCase, allowIntegerValues: false) }
    };
}

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
    public int MaxRequests { get; init; } = 5;
    public long MaxBytes { get; init; } = 2_000_000;
    public int TimeoutSeconds { get; init; } = 30;
    public int MinDelayMilliseconds { get; init; } = 1000;
    public string? ETag { get; init; }
    public DateTimeOffset? LastModified { get; init; }

    public void Validate()
    {
        if (Version != CollectionProtocol.Version)
            throw new ArgumentException("Unsupported collection protocol version.");
        if (string.IsNullOrWhiteSpace(JobId) || string.IsNullOrWhiteSpace(SourceId))
            throw new ArgumentException("Job and source IDs are required.");
        if (!Uri.TryCreate(AllowedOrigin, UriKind.Absolute, out var origin) ||
            origin.Scheme is not ("https" or "http") || origin.UserInfo.Length != 0 ||
            origin.AbsolutePath != "/" || origin.Query.Length != 0 || origin.Fragment.Length != 0)
            throw new ArgumentException("Allowed origin must be an HTTP(S) origin without credentials, path, query, or fragment.");
        if (string.IsNullOrEmpty(AllowedPathPrefix) || !AllowedPathPrefix.StartsWith('/') ||
            AllowedPathPrefix.Contains('?') || AllowedPathPrefix.Contains('#') || AllowedPathPrefix.Contains('%') ||
            AllowedPathPrefix.Contains('\\') || AllowedPathPrefix.Split('/').Any(s => s is "." or ".."))
            throw new ArgumentException("Allowed path prefix must be a plain absolute path.");
        if (!Uri.TryCreate(Url, UriKind.Absolute, out var url) || !Allows(url))
            throw new ArgumentException("Requested URL is outside its allowed scope or contains credentials or a fragment.");
        if (string.IsNullOrWhiteSpace(ArtifactDirectory) || !Path.IsPathFullyQualified(ArtifactDirectory))
            throw new ArgumentException("Artifact directory must be an absolute path.");
        if (MaxRequests is < 2 or > 20 || MaxBytes is < 1 or > 100_000_000 ||
            TimeoutSeconds is < 1 or > 300 || MinDelayMilliseconds is < 0 or > 60_000)
            throw new ArgumentException("Collection budgets are outside supported bounds.");
        if (ETag is not null && (!System.Net.Http.Headers.EntityTagHeaderValue.TryParse(ETag, out var tag) || tag.Tag == "*"))
            throw new ArgumentException("ETag must be a single entity tag.");
    }

    public bool Allows(Uri url)
    {
        if (!Uri.TryCreate(AllowedOrigin, UriKind.Absolute, out var origin) ||
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

public enum CollectionOutcome { Captured, NotModified, Deferred, Failed }

public sealed record CollectionResult
{
    [JsonRequired]
    public int Version { get; init; } = CollectionProtocol.Version;
    public required string JobId { get; init; }
    public required string SourceId { get; init; }
    public required string RequestedUrl { get; init; }
    public required string FinalUrl { get; init; }
    public required CollectionOutcome Outcome { get; init; }
    public required DateTimeOffset ObservedAt { get; init; }
    public int? HttpStatus { get; init; }
    public string? ETag { get; init; }
    public DateTimeOffset? LastModified { get; init; }
    public string? Sha256 { get; init; }
    public string? ArtifactPath { get; init; }
    public long? ArtifactBytes { get; init; }
    [JsonRequired]
    public long BytesReceived { get; init; }
    [JsonRequired]
    public int RequestCount { get; init; }
    public string? FailureCode { get; init; }
    public long? RetryAfterSeconds { get; init; }
}
