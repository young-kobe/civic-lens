using System.Text.Json;
using System.Text.Json.Nodes;
using CivicLens.Collection.Contracts;
using static CivicLens.Tests.Fixtures.CollectionFixtures;

namespace CivicLens.Tests.Collection.Contracts;

public sealed class CollectionProtocolTests
{
    [Fact]
    public void RobotsRedirectAccountingCannotInventAConditionalContentRequest()
    {
        var request = Request() with { ETag = "\"v1\"" };
        var receipt = Receipt(request) with
        {
            Outcome = CollectionOutcome.Deferred,
            FailureCode = CollectionFailureCode.RateLimited,
            Response = null,
            Capture = null,
            RobotsRequestCount = 2,
            RequestCount = 2,
            RetryAfterSeconds = 60
        };
        receipt.ValidateAgainst(request);
        Assert.Throws<InvalidDataException>(() => (receipt with
        {
            SentValidators = new HttpRequestValidators { ETag = request.ETag }
        }).ValidateAgainst(request));
        Assert.Throws<InvalidDataException>(() => (receipt with
        {
            Response = new HttpResponseMetadata { StatusCode = 429, ContentEncodings = [] }
        }).ValidateAgainst(request));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(3)]
    public void VersionSevenRejectsMissingOrImpossibleRobotsAccounting(int? robotsRequests)
    {
        var request = Request();
        Assert.Throws<InvalidDataException>(() => (Receipt(request) with
        {
            RobotsRequestCount = robotsRequests
        }).ValidateAgainst(request));
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void LegacyReceiptsCannotClaimNewRobotsEvidence(int version)
    {
        var request = Request() with { Version = version };
        var receipt = Receipt(request);
        receipt.ValidateAgainst(request);
        Assert.Throws<InvalidDataException>(() => (receipt with { RobotsCrawlDelayMilliseconds = 1 }).ValidateAgainst(request));
    }

    [Fact]
    public void CrawlDelayDeferralCannotShortenDeclaredWait()
    {
        var request = Request();
        var receipt = Receipt(request) with
        {
            Outcome = CollectionOutcome.Deferred,
            FailureCode = CollectionFailureCode.CrawlDelay,
            Response = null,
            Capture = null,
            RequestCount = 1,
            RobotsCrawlDelayMilliseconds = 1501,
            RetryAfterSeconds = 2
        };
        receipt.ValidateAgainst(request);
        Assert.Throws<InvalidDataException>(() => (receipt with { RetryAfterSeconds = 1 }).ValidateAgainst(request));
        Assert.Throws<InvalidDataException>(() => (receipt with { RobotsCrawlDelayMilliseconds = long.MaxValue }).ValidateAgainst(request));
    }

    [Fact]
    public void RequestsRejectSerializedManifestOverflowIncludingJsonEscaping()
    {
        var basis = Request() with { SourceId = "" };
        var overhead = JsonSerializer.SerializeToUtf8Bytes(basis, CollectionProtocol.JsonOptions).Length;
        var maximum = basis with { SourceId = new string('s', CollectionProtocol.MaximumManifestBytes - overhead) };
        maximum.Validate();
        Assert.Throws<ArgumentException>(() => (maximum with { SourceId = maximum.SourceId + "s" }).Validate());
        var escaped = basis with { SourceId = new string('é', 12_000) };
        Assert.True(escaped.SourceId.Length < CollectionProtocol.MaximumManifestBytes);
        Assert.Throws<ArgumentException>(escaped.Validate);
    }

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
    public void VersionSevenIsCurrentAndVersionThreeRemainsValidForPageRecovery()
    {
        var requestJson = JsonSerializer.Serialize(Request(), CollectionProtocol.JsonOptions);
        Assert.Contains("\"version\":7", requestJson, StringComparison.Ordinal);
        var resultJson = JsonSerializer.Serialize(Receipt(Request()), CollectionProtocol.JsonOptions);
        Assert.Contains("\"failureCode\":null", resultJson, StringComparison.Ordinal);
        var roundTrip = JsonSerializer.Serialize(JsonSerializer.Deserialize<CollectionResult>(resultJson, CollectionProtocol.JsonOptions), CollectionProtocol.JsonOptions);
        Assert.Equal(resultJson, roundTrip);
        var v3Request = Request() with { Version = 3 };
        v3Request.Validate();
        var v3Result = Receipt(v3Request) with { Version = 3 };
        v3Result.ValidateAgainst(v3Request);
        Assert.Throws<ArgumentException>(() => (v3Request with { Mode = CollectionMode.Feed }).Validate());
        Assert.Throws<ArgumentException>(() => (v3Request with { MaxCandidates = 5 }).Validate());
        foreach (var version in new[] { 1, 2 })
        {
            var oldRequest = requestJson.Replace("\"version\":7", $"\"version\":{version}", StringComparison.Ordinal);
            Assert.Throws<ArgumentException>(() => JsonSerializer.Deserialize<CollectionRequest>(oldRequest, CollectionProtocol.JsonOptions)!.Validate());
            Assert.Throws<InvalidDataException>(() => (Receipt(Request()) with { Version = version }).ValidateAgainst(Request()));
        }
    }

    [Fact]
    public void VersionSixRequestAndReceiptRemainValidForRecovery()
    {
        var request = Request() with { Version = 6 };
        var requestJson = JsonSerializer.Serialize(request, CollectionProtocol.JsonOptions);
        var recoveredRequest = JsonSerializer.Deserialize<CollectionRequest>(requestJson, CollectionProtocol.JsonOptions)!;
        recoveredRequest.Validate();
        Assert.Equal(6, recoveredRequest.Version);

        var receiptJson = JsonSerializer.Serialize(Receipt(request), CollectionProtocol.JsonOptions);
        var recoveredReceipt = JsonSerializer.Deserialize<CollectionResult>(receiptJson, CollectionProtocol.JsonOptions)!;
        recoveredReceipt.ValidateAgainst(recoveredRequest);
        Assert.Equal(6, recoveredReceipt.Version);
        Assert.Equal(1, recoveredReceipt.RobotsRequestCount);
    }

    [Theory]
    [InlineData("\"version\":7", "\"version\":99")]
    [InlineData("\"version\":7", "\"version\":7,\"version\":7")]
    [InlineData("\"version\":7", "\"version\":7,\"extra\":true")]
    [InlineData("\"sourceId\":\"source\"", "\"sourceId\":null")]
    public void MalformedOrUnsupportedWireContractsAreRejected(string original, string replacement)
    {
        var json = JsonSerializer.Serialize(Request(), CollectionProtocol.JsonOptions).Replace(original, replacement, StringComparison.Ordinal);
        var exception = Record.Exception(() => JsonSerializer.Deserialize<CollectionRequest>(json, CollectionProtocol.JsonOptions)!.Validate());
        Assert.True(exception is JsonException or ArgumentException, $"Expected rejection, got {exception}");
    }

    [Theory]
    [InlineData(4, CollectionMode.Feed)]
    [InlineData(5, CollectionMode.Feed)]
    [InlineData(5, CollectionMode.Html)]
    public void DiscoveryWireRecoveryPreservesSupportedVersionsAndRequiresCapturedCandidates(int version, CollectionMode mode)
    {
        var request = Request() with { Version = version, Mode = mode };
        var result = Receipt(request) with
        {
            Version = version,
            Discovery = new DiscoveryResult { Status = DiscoveryStatus.Parsed, Urls = ["https://example.test/pages/article?q=1"] }
        };
        var json = JsonSerializer.Serialize(result, CollectionProtocol.JsonOptions);
        var restored = JsonSerializer.Deserialize<CollectionResult>(json, CollectionProtocol.JsonOptions)!;
        restored.ValidateAgainst(request);
        Assert.Equal(version, restored.Version);
        Assert.Equal(result.Discovery.Urls, restored.Discovery!.Urls);
        Assert.Throws<InvalidDataException>(() => (restored with { Discovery = null }).ValidateAgainst(request));
        Assert.Throws<InvalidDataException>(() => (restored with
        {
            Outcome = CollectionOutcome.Failed,
            Capture = null,
            FailureCode = CollectionFailureCode.HttpError
        }).ValidateAgainst(request));
        Assert.Throws<InvalidDataException>(() => (restored with
        {
            Discovery = result.Discovery with { Status = DiscoveryStatus.LimitExceeded }
        }).ValidateAgainst(request));
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    public void HtmlModeCannotBeSmuggledIntoAnOlderProtocol(int version) =>
        Assert.Throws<ArgumentException>(() => (Request() with { Version = version, Mode = CollectionMode.Html }).Validate());

    [Theory]
    [InlineData("https://example.test/pages/a#fragment")]
    [InlineData("https://foreign.test/pages/a")]
    [InlineData("https://example.test/private")]
    public void DiscoveryReceiptCannotBypassScopeValidation(string url)
    {
        var request = Request() with { Mode = CollectionMode.Html };
        var result = Receipt(request) with { Discovery = new DiscoveryResult { Status = DiscoveryStatus.Parsed, Urls = [url] } };
        Assert.Throws<InvalidDataException>(() => result.ValidateAgainst(request));
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
