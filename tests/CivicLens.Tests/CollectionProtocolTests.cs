using System.Text.Json;
using CivicLens.Application;
using CivicLens.Collection.Contracts;

namespace CivicLens.Tests;

public sealed class CollectionProtocolTests
{
    [Theory]
    [InlineData("https://example.test/other")]
    [InlineData("https://example.test/pages-other/a")]
    [InlineData("https://other.test/pages/a")]
    [InlineData("http://example.test/pages/a")]
    [InlineData("https://example.test:444/pages/a")]
    [InlineData("https://user:secret@example.test/pages/a")]
    [InlineData("https://example.test/pages/a#fragment")]
    [InlineData("https://example.test/pages/%252e%252e/private")]
    [InlineData("https://example.test/pages/%2e%2e/private")]
    public void RequestsRejectDestinationsOutsideExplicitScope(string url) =>
        Assert.Throws<ArgumentException>(() => (Request() with { Url = url }).Validate());

    [Theory]
    [InlineData("https://example.test/pages")]
    [InlineData("https://example.test/pages/a?x=1")]
    public void ScopeAllowsExactPathOrDescendants(string url) => (Request() with { Url = url }).Validate();

    [Theory]
    [InlineData("\"version\":1,", "")]
    [InlineData("\"version\":1", "\"version\":99")]
    [InlineData("\"version\":1", "\"version\":1,\"version\":1")]
    [InlineData("\"version\":1", "\"version\":1,\"extra\":true")]
    [InlineData("\"sourceId\":\"source\"", "\"sourceId\":null")]
    public void MalformedOrUnsupportedWireContractsAreRejected(string original, string replacement)
    {
        var json = JsonSerializer.Serialize(Request(), CollectionProtocol.JsonOptions).Replace(original, replacement, StringComparison.Ordinal);
        var exception = Record.Exception(() => JsonSerializer.Deserialize<CollectionRequest>(json, CollectionProtocol.JsonOptions)!.Validate());
        Assert.True(exception is JsonException or ArgumentException, $"Expected rejection, got {exception}");
    }

    [Fact]
    public void MalformedUtf8CannotChangeIdentityDuringDeserialization()
    {
        var json = System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(Request(), CollectionProtocol.JsonOptions));
        var offset = System.Text.Encoding.UTF8.GetString(json).IndexOf("sourceId", StringComparison.Ordinal) + "sourceId\":\"".Length;
        json[offset] = 0xff;
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<CollectionRequest>(json, CollectionProtocol.JsonOptions));
    }

    [Fact]
    public void ReceiptMustMatchRequestAndCaptureMetadata()
    {
        var request = Request();
        var result = Receipt(request);
        CollectWatchedPage.ValidateResult(request, result);
        Assert.Throws<InvalidDataException>(() => CollectWatchedPage.ValidateResult(request, result with { JobId = "other" }));
        Assert.Throws<InvalidDataException>(() => CollectWatchedPage.ValidateResult(request, result with { Version = 2 }));
        Assert.Throws<InvalidDataException>(() => CollectWatchedPage.ValidateResult(request, result with { ArtifactPath = "../capture.gz" }));
        Assert.Throws<InvalidDataException>(() => CollectWatchedPage.ValidateResult(request, result with { ArtifactBytes = result.BytesReceived + 1 }));
        Assert.Throws<InvalidDataException>(() => CollectWatchedPage.ValidateResult(request, result with { BytesReceived = request.MaxBytes + 1 }));
        Assert.Throws<InvalidDataException>(() => CollectWatchedPage.ValidateResult(request, result with { Outcome = CollectionOutcome.Failed, FailureCode = "incomplete" }));
    }

    [Fact]
    public void NotModifiedWithoutPriorValidatorIsRejected()
    {
        var request = Request();
        var result = Receipt(request) with
        {
            Outcome = CollectionOutcome.NotModified,
            HttpStatus = 304,
            Sha256 = null,
            ArtifactPath = null,
            ArtifactBytes = null
        };
        Assert.Throws<InvalidDataException>(() => CollectWatchedPage.ValidateResult(request, result));
        CollectWatchedPage.ValidateResult(request with { ETag = "\"v1\"" }, result);
    }

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
        JobId = request.JobId,
        SourceId = request.SourceId,
        RequestedUrl = request.Url,
        FinalUrl = request.Url,
        Outcome = CollectionOutcome.Captured,
        ObservedAt = DateTimeOffset.UtcNow,
        HttpStatus = 200,
        Sha256 = new string('a', 64),
        ArtifactPath = new string('a', 64) + ".gz",
        ArtifactBytes = 4,
        BytesReceived = 10,
        RequestCount = 2
    };
}
