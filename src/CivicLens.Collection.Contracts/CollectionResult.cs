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
    InvalidResponse
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
    public long BytesReceived { get; init; }
    [JsonRequired]
    public int RequestCount { get; init; }
    public CaptureArtifact? Capture { get; init; }
    public CollectionFailureCode? FailureCode { get; init; }
    public long? RetryAfterSeconds { get; init; }

    public void ValidateAgainst(CollectionRequest request)
    {
        if (request is null)
            throw new InvalidDataException("Collection request is required for result validation.");
        request.Validate();
        ValidateIdentity(request);
        ValidateObservation(request);
        ValidateResourceAccounting(request);
        Response?.Validate();
        Capture?.Validate();
        ValidateOutcome(request);
    }

    private void ValidateIdentity(CollectionRequest request)
    {
        if (Version != CollectionProtocol.Version)
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

    private void ValidateResourceAccounting(CollectionRequest request)
    {
        if (BytesReceived < 0 || BytesReceived > request.MaxBytes || RequestCount < 0 ||
            RequestCount > request.MaxRequests || RetryAfterSeconds is < 0)
            throw new InvalidDataException("Collector result exceeds its request budgets.");
        if ((Response is not null && RequestCount < 2) || (BytesReceived > 0 && RequestCount == 0))
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
                ValidateDeferred();
                break;
            case CollectionOutcome.Failed:
                ValidateFailed();
                break;
            default:
                throw new InvalidDataException("Collector result outcome is invalid.");
        }
    }

    private void ValidateCaptured()
    {
        if (Response?.StatusCode != 200 || Capture is null || RequestCount < 2 || FailureCode is not null ||
            RetryAfterSeconds is not null || Capture.ByteLength > BytesReceived)
            throw new InvalidDataException("Captured result has invalid response or artifact metadata.");
    }

    private void ValidateNotModified(CollectionRequest request)
    {
        if (Response?.StatusCode != 304 || !Uri.TryCreate(FinalUrl, UriKind.Absolute, out var final) ||
            final != new Uri(request.Url) || (request.ETag is null && request.LastModified is null) ||
            RequestCount < 2 || Capture is not null || FailureCode is not null || RetryAfterSeconds is not null)
            throw new InvalidDataException("Not-modified result requires a conditional request and HTTP 304.");
    }

    private void ValidateDeferred()
    {
        if (Response?.StatusCode != 429 || RequestCount < 2 || Capture is not null ||
            FailureCode != CollectionFailureCode.RateLimited)
            throw new InvalidDataException("Deferred result requires a rate-limited content request.");
    }

    private void ValidateFailed()
    {
        if (Capture is not null || RetryAfterSeconds is not null || FailureCode is null ||
            FailureCode == CollectionFailureCode.RateLimited)
            throw new InvalidDataException("Failed result has invalid failure metadata.");
    }
}
