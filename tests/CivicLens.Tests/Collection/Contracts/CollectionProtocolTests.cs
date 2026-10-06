using System.Text.Json;
using System.Text.Json.Nodes;
using CivicLens.Collection.Contracts;
using static CivicLens.Tests.Fixtures.CollectionFixtures;

namespace CivicLens.Tests.Collection.Contracts;

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

    [Fact]
    public void VersionThreeRoundTripsExplicitlyAndOlderVersionsAreRejected()
    {
        var requestJson = JsonSerializer.Serialize(Request(), CollectionProtocol.JsonOptions);
        Assert.Contains("\"version\":3", requestJson, StringComparison.Ordinal);
        var resultJson = JsonSerializer.Serialize(Receipt(Request()), CollectionProtocol.JsonOptions);
        Assert.Contains("\"failureCode\":null", resultJson, StringComparison.Ordinal);
        var roundTrip = JsonSerializer.Serialize(JsonSerializer.Deserialize<CollectionResult>(resultJson, CollectionProtocol.JsonOptions), CollectionProtocol.JsonOptions);
        Assert.Equal(resultJson, roundTrip);
        foreach (var version in new[] { 1, 2 })
        {
            var oldRequest = requestJson.Replace("\"version\":3", $"\"version\":{version}", StringComparison.Ordinal);
            Assert.Throws<ArgumentException>(() => JsonSerializer.Deserialize<CollectionRequest>(oldRequest, CollectionProtocol.JsonOptions)!.Validate());
            Assert.Throws<InvalidDataException>(() => (Receipt(Request()) with { Version = version }).ValidateAgainst(Request()));
        }
    }

    [Theory]
    [InlineData("\"version\":3", "\"version\":99")]
    [InlineData("\"version\":3", "\"version\":3,\"version\":3")]
    [InlineData("\"version\":3", "\"version\":3,\"extra\":true")]
    [InlineData("\"sourceId\":\"source\"", "\"sourceId\":null")]
    public void MalformedOrUnsupportedWireContractsAreRejected(string original, string replacement)
    {
        var json = JsonSerializer.Serialize(Request(), CollectionProtocol.JsonOptions).Replace(original, replacement, StringComparison.Ordinal);
        var exception = Record.Exception(() => JsonSerializer.Deserialize<CollectionRequest>(json, CollectionProtocol.JsonOptions)!.Validate());
        Assert.True(exception is JsonException or ArgumentException, $"Expected rejection, got {exception}");
    }

    [Fact]
    public void FailureCodeRejectsUnknownNamesAndNumericValues()
    {
        var result = Receipt(Request()) with { Outcome = CollectionOutcome.Failed, Capture = null, FailureCode = CollectionFailureCode.HttpError };
        var json = JsonSerializer.Serialize(result, CollectionProtocol.JsonOptions);
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<CollectionResult>(json.Replace("httpError", "invented", StringComparison.Ordinal), CollectionProtocol.JsonOptions));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<CollectionResult>(json.Replace("\"httpError\"", "99", StringComparison.Ordinal), CollectionProtocol.JsonOptions));
    }

    [Fact]
    public void MissingNestedRequiredFieldsAndNullEncodingEntriesFailClearly()
    {
        var json = JsonSerializer.Serialize(Receipt(Request()), CollectionProtocol.JsonOptions);
        var missingEncodings = JsonNode.Parse(json)!;
        missingEncodings["response"]!.AsObject().Remove("contentEncodings");
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<CollectionResult>(missingEncodings.ToJsonString(), CollectionProtocol.JsonOptions));
        var missingArtifactLength = JsonNode.Parse(json)!;
        missingArtifactLength["capture"]!.AsObject().Remove("byteLength");
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<CollectionResult>(missingArtifactLength.ToJsonString(), CollectionProtocol.JsonOptions));
        var missingVersion = JsonNode.Parse(JsonSerializer.Serialize(Request(), CollectionProtocol.JsonOptions))!;
        missingVersion.AsObject().Remove("version");
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<CollectionRequest>(missingVersion.ToJsonString(), CollectionProtocol.JsonOptions));
        Assert.Throws<InvalidDataException>(() => (Receipt(Request()) with
        {
            Response = new HttpResponseMetadata { StatusCode = 200, ContentEncodings = null! }
        }).ValidateAgainst(Request()));
        Assert.Throws<InvalidDataException>(() => (Receipt(Request()) with
        {
            Response = new HttpResponseMetadata { StatusCode = 200, ContentEncodings = [null!] }
        }).ValidateAgainst(Request()));
    }

    [Fact]
    public void ResultMustMatchRequestAndItsCaptureMetadata()
    {
        var request = Request();
        var result = Receipt(request);
        result.ValidateAgainst(request);
        Assert.Throws<InvalidDataException>(() => (result with { JobId = "other" }).ValidateAgainst(request));
        Assert.Throws<InvalidDataException>(() => (result with { Version = 1 }).ValidateAgainst(request));
        Assert.Throws<InvalidDataException>(() => (result with { FinalUrl = "https://example.test/private" }).ValidateAgainst(request));
        Assert.Throws<InvalidDataException>(() => (result with { Capture = result.Capture! with { RelativePath = "../capture.gz" } }).ValidateAgainst(request));
        Assert.Throws<InvalidDataException>(() => (result with { Capture = result.Capture! with { ByteLength = result.BytesReceived + 1 } }).ValidateAgainst(request));
        Assert.Throws<InvalidDataException>(() => (result with { BytesReceived = request.MaxBytes + 1 }).ValidateAgainst(request));
        Assert.Throws<InvalidDataException>(() => (result with { Outcome = CollectionOutcome.Failed, FailureCode = CollectionFailureCode.IncompleteResponse }).ValidateAgainst(request));
    }

    [Fact]
    public void OutcomeShapeRequiresMatchingResponseAndFailureFields()
    {
        var request = Request();
        var captured = Receipt(request);
        Assert.Throws<InvalidDataException>(() => (captured with { Response = captured.Response! with { StatusCode = 201 } }).ValidateAgainst(request));
        Assert.Throws<InvalidDataException>(() => (captured with { Capture = null }).ValidateAgainst(request));
        Assert.Throws<InvalidDataException>(() => (captured with { FailureCode = CollectionFailureCode.HttpError }).ValidateAgainst(request));

        var deferred = captured with { Outcome = CollectionOutcome.Deferred, Response = captured.Response! with { StatusCode = 429 }, Capture = null, FailureCode = CollectionFailureCode.RateLimited, RetryAfterSeconds = 15 };
        deferred.ValidateAgainst(request);
        Assert.Throws<InvalidDataException>(() => (deferred with { FailureCode = CollectionFailureCode.HttpError }).ValidateAgainst(request));
        Assert.Throws<InvalidDataException>(() => (deferred with { Outcome = CollectionOutcome.Failed }).ValidateAgainst(request));

        var failed = captured with { Outcome = CollectionOutcome.Failed, Capture = null, FailureCode = CollectionFailureCode.InvalidResponse, Response = null };
        failed.ValidateAgainst(request);
    }

    [Fact]
    public void NotModifiedRequiresPriorValidatorAndOriginalUrl()
    {
        var request = Request();
        var result = Receipt(request) with
        {
            Outcome = CollectionOutcome.NotModified,
            Response = new HttpResponseMetadata { StatusCode = 304, ContentEncodings = [] },
            Capture = null
        };
        Assert.Throws<InvalidDataException>(() => result.ValidateAgainst(request));
        Assert.Throws<InvalidDataException>(() => result.ValidateAgainst(request with { ETag = "\"v1\"" }));
        result = result with { SentValidators = new HttpRequestValidators { ETag = "\"v1\"" } };
        result.ValidateAgainst(request with { ETag = "\"v1\"" });
        Assert.Throws<InvalidDataException>(() => (result with { FinalUrl = "https://example.test/pages/redirected" }).ValidateAgainst(request with { ETag = "\"v1\"" }));
    }

    [Fact]
    public void SentValidatorsAreRequiredOnWireAndMustMatchAttemptedRequest()
    {
        var request = Request() with { ETag = "  \"v1\"  " };
        var result = Receipt(request) with { SentValidators = new HttpRequestValidators { ETag = "\"v1\"" } };
        result.ValidateAgainst(request);
        Assert.Throws<InvalidDataException>(() => (result with { SentValidators = null }).ValidateAgainst(request));
        Assert.Throws<InvalidDataException>(() => (result with
        {
            SentValidators = new HttpRequestValidators { ETag = request.ETag }
        }).ValidateAgainst(request));
        Assert.Throws<InvalidDataException>(() => (result with
        {
            SentValidators = new HttpRequestValidators { ETag = "\"other\"" }
        }).ValidateAgainst(request));
        Assert.Throws<InvalidDataException>(() => (result with
        {
            Outcome = CollectionOutcome.Failed,
            Response = null,
            Capture = null,
            FailureCode = CollectionFailureCode.RobotsDenied,
            BytesReceived = 0,
            RequestCount = 1
        }).ValidateAgainst(request));
        Assert.Throws<InvalidDataException>(() => (result with
        {
            FinalUrl = "https://example.test/pages/redirected"
        }).ValidateAgainst(request));

        var json = JsonNode.Parse(JsonSerializer.Serialize(result, CollectionProtocol.JsonOptions))!;
        json.AsObject().Remove("sentValidators");
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<CollectionResult>(json.ToJsonString(), CollectionProtocol.JsonOptions));
        json = JsonNode.Parse(JsonSerializer.Serialize(result, CollectionProtocol.JsonOptions))!;
        json["sentValidators"]!.AsObject().Remove("lastModified");
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<CollectionResult>(json.ToJsonString(), CollectionProtocol.JsonOptions));
    }

    [Fact]
    public void SentLastModifiedMustUseWholeSecondsAtUtcWirePrecision()
    {
        var modified = new DateTimeOffset(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);
        var request = Request() with { LastModified = modified.AddTicks(1234567) };
        var result = Receipt(request) with { SentValidators = new HttpRequestValidators { LastModified = modified } };
        result.ValidateAgainst(request);
        Assert.Throws<InvalidDataException>(() => (result with
        {
            SentValidators = new HttpRequestValidators { LastModified = request.LastModified }
        }).ValidateAgainst(request));
        Assert.Throws<InvalidDataException>(() => (result with
        {
            SentValidators = new HttpRequestValidators { LastModified = modified.ToOffset(TimeSpan.FromHours(2)) }
        }).ValidateAgainst(request));
    }

    [Fact]
    public void ResponseHeadersAndArtifactMetadataAreBounded()
    {
        var response = new HttpResponseMetadata { StatusCode = 200, ETag = "W/\"v1\"", ContentType = "text/html; charset=utf-8", ContentEncodings = ["br", "vendor-opaque"] };
        response.Validate();
        Assert.Throws<InvalidDataException>(() => (response with { ContentType = "text/html\r\ninjected" }).Validate());
        Assert.Throws<InvalidDataException>(() => (response with { ContentEncodings = ["bad coding"] }).Validate());
        Assert.Throws<InvalidDataException>(() => (response with { ContentEncodings = Enumerable.Repeat("x", 17).ToArray() }).Validate());
        Assert.Throws<InvalidDataException>(() => new CaptureArtifact { Sha256 = new string('A', 64), RelativePath = new string('A', 64) + ".gz", ByteLength = 0 }.Validate());
    }

    [Fact]
    public void MalformedUtf8CannotChangeIdentityDuringDeserialization()
    {
        var json = System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(Request(), CollectionProtocol.JsonOptions));
        var offset = System.Text.Encoding.UTF8.GetString(json).IndexOf("sourceId", StringComparison.Ordinal) + "sourceId\":\"".Length;
        json[offset] = 0xff;
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<CollectionRequest>(json, CollectionProtocol.JsonOptions));
    }

}
