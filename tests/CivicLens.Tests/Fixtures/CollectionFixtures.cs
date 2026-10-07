using CivicLens.Collection.Contracts;

namespace CivicLens.Tests.Fixtures;

internal static class CollectionFixtures
{
    internal static CollectionRequest Request() => new()
    {
        JobId = "job",
        SourceId = "source",
        Url = "https://example.test/pages/a",
        AllowedOrigin = "https://example.test",
        AllowedPathPrefix = "/pages",
        ArtifactDirectory = Path.GetFullPath(".runtime/test-captures")
    };

    internal static CollectionResult Receipt(CollectionRequest request) => new()
    {
        Version = request.Version,
        RobotsRequestCount = request.Version >= 6 ? 1 : null,
        JobId = request.JobId,
        SourceId = request.SourceId,
        RequestedUrl = request.Url,
        FinalUrl = request.Url,
        Outcome = CollectionOutcome.Captured,
        ObservedAt = DateTimeOffset.UtcNow,
        Response = new HttpResponseMetadata { StatusCode = 200, ETag = "\"v1\"", ContentEncodings = [] },
        Capture = new CaptureArtifact { Sha256 = new string('a', 64), RelativePath = new string('a', 64) + ".gz", ByteLength = 4 },
        BytesReceived = 10,
        RequestCount = 2
    };
}
