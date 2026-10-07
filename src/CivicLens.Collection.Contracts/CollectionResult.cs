using System.Text.Json.Serialization;

namespace CivicLens.Collection.Contracts;

public enum CollectionOutcome
{
    Captured,
    NotModified,
    Deferred,
    Failed
}

public enum CollectionFailureCode
{
    RobotsUnavailable,
    RobotsDenied,
    OutOfScope,
    UnexpectedNotModified,
    RateLimited,
    RedirectMissingLocation,
    HttpError,
    Cancelled,
    Timeout,
    RequestBudget,
    Oversized,
    TransportError,
    ArtifactWriteFailed,
    IncompleteResponse,
    InvalidResponse,
    CrawlDelay
}

/// <summary>An observation of one collection job. ObservedAt is not a document version timestamp.</summary>
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
    public HttpResponseMetadata? Response { get; init; }
    [JsonRequired]
    public HttpRequestValidators? SentValidators { get; init; }
    [JsonRequired]
    public long BytesReceived { get; init; }
    [JsonRequired]
    public int RequestCount { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? RobotsRequestCount { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? RobotsCrawlDelayMilliseconds { get; init; }
    public CaptureArtifact? Capture { get; init; }
    public DiscoveryResult? Discovery { get; init; }
    public CollectionFailureCode? FailureCode { get; init; }
    public long? RetryAfterSeconds { get; init; }

    public void ValidateAgainst(CollectionRequest request)
    {
        if (request is null)
            throw new InvalidDataException("Collection request is required for result validation.");
        request.Validate();
        ValidateIdentity(request);
        ValidateObservation(request);
        ValidateRobotsAccounting();
        ValidateResourceAccounting(request);
        ValidateSentValidators(request);
        Response?.Validate();
        Capture?.Validate();
        if (Discovery is not null)
        {
            if (request.Mode == CollectionMode.Page || Outcome != CollectionOutcome.Captured)
                throw new InvalidDataException("Discovery is only valid for captured discovery requests.");
            Discovery.ValidateAgainst(request);
        }
        else if (Outcome == CollectionOutcome.Captured && request.Mode != CollectionMode.Page)
            throw new InvalidDataException("Captured discovery result requires discovery metadata.");
        ValidateOutcome(request);
    }

    private void ValidateIdentity(CollectionRequest request)
    {
        if (Version != request.Version || !CollectionProtocol.IsSupported(Version))
            throw new InvalidDataException("Unsupported collection result version.");
        if (JobId != request.JobId || SourceId != request.SourceId || RequestedUrl != request.Url)
            throw new InvalidDataException("Collector result does not identify the requested work.");
    }

    private void ValidateObservation(CollectionRequest request)
    {
        if (!Uri.TryCreate(FinalUrl, UriKind.Absolute, out var final) || !request.Allows(final))
            throw new InvalidDataException("Collector result destination is outside the requested scope.");
        if (ObservedAt == default || (FailureCode.HasValue && !Enum.IsDefined(FailureCode.Value)))
            throw new InvalidDataException("Collector result observation metadata is invalid.");
    }

    private int ContentRequestCount => Version >= 6 ? RequestCount - RobotsRequestCount!.Value : Math.Max(0, RequestCount - 1);

    private void ValidateRobotsAccounting()
    {
        if (Version < 6)
        {
            if (RobotsRequestCount is not null || RobotsCrawlDelayMilliseconds is not null || FailureCode == CollectionFailureCode.CrawlDelay)
                throw new InvalidDataException("Robots accounting requires collection protocol version 6.");
            return;
        }
        if (RobotsRequestCount is null or < 0 || RobotsRequestCount > RequestCount ||
            (RequestCount > 0 && RobotsRequestCount == 0) ||
            RobotsCrawlDelayMilliseconds is < 0 or > CollectionProtocol.MaximumCrawlDelayMilliseconds ||
            (RobotsCrawlDelayMilliseconds is not null && RobotsRequestCount == 0))
            throw new InvalidDataException("Robots accounting or crawl delay is invalid.");
    }

    private void ValidateResourceAccounting(CollectionRequest request)
    {
        if (BytesReceived < 0 || BytesReceived > request.MaxBytes || RequestCount < 0 ||
            RequestCount > request.MaxRequests || RetryAfterSeconds is < 0)
            throw new InvalidDataException("Collector result exceeds its request budgets.");
        if ((Response is not null && ContentRequestCount < 1) || (BytesReceived > 0 && RequestCount == 0))
            throw new InvalidDataException("Response metadata and received bytes require corresponding requests.");
    }

    private void ValidateOutcome(CollectionRequest request)
    {
        switch (Outcome)
        {
            case CollectionOutcome.Captured:
                ValidateCaptured();
                break;
            case CollectionOutcome.NotModified:
                ValidateNotModified(request);
                break;
            case CollectionOutcome.Deferred:
                ValidateDeferred(request);
                break;
            case CollectionOutcome.Failed:
                ValidateFailed();
                break;
            default:
                throw new InvalidDataException("Collector result outcome is invalid.");
        }
    }

    private void ValidateSentValidators(CollectionRequest request)
    {
        var conditionalAttempt = ContentRequestCount >= 1 && new Uri(FinalUrl) == new Uri(request.Url) &&
            (request.ETag is not null || request.LastModified is not null);
        if (conditionalAttempt != (SentValidators is not null))
            throw new InvalidDataException("Sent validators must describe the last attempted content request.");
        SentValidators?.ValidateAgainst(request);
    }

    private void ValidateCaptured()
    {
        if (Response?.StatusCode != 200 || Capture is null || ContentRequestCount < 1 || FailureCode is not null ||
            RetryAfterSeconds is not null || Capture.ByteLength > BytesReceived)
            throw new InvalidDataException("Captured result has invalid response or artifact metadata.");
    }

    private void ValidateNotModified(CollectionRequest request)
    {
        if (Response?.StatusCode != 304 || !Uri.TryCreate(FinalUrl, UriKind.Absolute, out var final) ||
            final != new Uri(request.Url) || SentValidators is null ||
            ContentRequestCount < 1 || Capture is not null || FailureCode is not null || RetryAfterSeconds is not null)
            throw new InvalidDataException("Not-modified result requires a conditional request and HTTP 304.");
    }

    private void ValidateDeferred(CollectionRequest request)
    {
        if (Capture is not null || FailureCode is null)
            throw new InvalidDataException("Deferred result has invalid failure metadata.");
        if (FailureCode == CollectionFailureCode.RateLimited && Response?.StatusCode == 429 && ContentRequestCount >= 1)
            return;
        if (Version >= 6 && FailureCode == CollectionFailureCode.CrawlDelay &&
            Math.Max(request.MinDelayMilliseconds, RobotsCrawlDelayMilliseconds ?? 0) > 0 &&
            RetryAfterSeconds >= (Math.Max(request.MinDelayMilliseconds, RobotsCrawlDelayMilliseconds ?? 0) + 999) / 1000 &&
            RobotsRequestCount > 0 &&
            (Response is null || Response.StatusCode is >= 300 and < 400))
            return;
        if (Version >= 6 && FailureCode is CollectionFailureCode.RateLimited or CollectionFailureCode.RobotsUnavailable &&
            ContentRequestCount == 0 && RobotsRequestCount > 0 && Response is null)
            return;
        throw new InvalidDataException("Deferred result requires rate limiting, robots unavailability, or crawl delay.");
    }

    private void ValidateFailed()
    {
        if (Capture is not null || RetryAfterSeconds is not null || FailureCode is null ||
            FailureCode is CollectionFailureCode.RateLimited or CollectionFailureCode.CrawlDelay)
            throw new InvalidDataException("Failed result has invalid failure metadata.");
    }
}
