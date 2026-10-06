using CivicLens.Collection.Contracts;

namespace CivicLens.Application;

public interface ICollectorProcess
{
    Task<CollectionResult> RunAsync(CollectionRequest request, CancellationToken cancellationToken);
}

public sealed class CollectWatchedPage(ICollectorProcess collector)
{
    public async Task<CollectionResult> ExecuteAsync(CollectionConfiguration configuration, string sourceId,
        string artifactDirectory, CancellationToken cancellationToken = default)
    {
        var request = configuration.CreateRequest(sourceId, Guid.NewGuid().ToString("N"), Path.GetFullPath(artifactDirectory));
        var result = await collector.RunAsync(request, cancellationToken);
        ValidateResult(request, result);
        return result;
    }

    public static void ValidateResult(CollectionRequest request, CollectionResult result)
    {
        if (result.Version != CollectionProtocol.Version || result.JobId != request.JobId ||
            result.SourceId != request.SourceId || result.RequestedUrl != request.Url ||
            !Uri.TryCreate(result.FinalUrl, UriKind.Absolute, out var final) || !request.Allows(final) ||
            !Enum.IsDefined(result.Outcome) || result.ObservedAt == default ||
            result.BytesReceived < 0 || result.BytesReceived > request.MaxBytes ||
            result.RequestCount < 0 || result.RequestCount > request.MaxRequests ||
            result.HttpStatus is < 100 or > 599 || result.RetryAfterSeconds is < 0)
            throw new InvalidDataException("Collector result does not match the request or its budgets.");

        if (result.Outcome == CollectionOutcome.Captured)
        {
            if (result.HttpStatus != 200 || result.RequestCount < 2 || result.FailureCode is not null ||
                result.RetryAfterSeconds is not null || result.ArtifactBytes is null or < 0 ||
                result.ArtifactBytes > result.BytesReceived || result.Sha256 is not { Length: 64 } hash ||
                hash.Any(c => !char.IsAsciiHexDigitLower(c)) || result.ArtifactPath != hash + ".gz")
                throw new InvalidDataException("Captured result has invalid artifact metadata.");
        }
        else if (result.Sha256 is not null || result.ArtifactPath is not null || result.ArtifactBytes is not null)
            throw new InvalidDataException("An unsuccessful observation cannot reference a capture.");

        if (result.Outcome == CollectionOutcome.NotModified &&
            (result.HttpStatus != 304 || final != new Uri(request.Url) || (request.ETag is null && request.LastModified is null) ||
             result.FailureCode is not null || result.RetryAfterSeconds is not null || result.RequestCount < 2))
            throw new InvalidDataException("Not-modified result requires a conditional request and HTTP 304.");
        if (result.Outcome == CollectionOutcome.Deferred &&
            (result.HttpStatus != 429 || result.FailureCode != "rateLimited" || result.RequestCount < 2))
            throw new InvalidDataException("Deferred result requires a rate-limited content request.");
        if (result.ETag is not null &&
            (!System.Net.Http.Headers.EntityTagHeaderValue.TryParse(result.ETag, out var tag) || tag.Tag == "*"))
            throw new InvalidDataException("Receipt ETag is invalid.");
        if (result.Outcome is CollectionOutcome.Failed or CollectionOutcome.Deferred && string.IsNullOrWhiteSpace(result.FailureCode))
            throw new InvalidDataException("Unsuccessful result requires a failure code.");
    }
}
