using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using CivicLens.Collection.Contracts;
using CivicLens.Collector.Http;

namespace CivicLens.Tests.Collector.Http;

public sealed class CollectorTests
{
    [Fact]
    public async Task CapturesRawBodyAsVerifiedContentAddressedGzip()
    {
        var payload = Encoding.UTF8.GetBytes("raw bytes\n");
        var handler = new QueueHandler(_ => Response(HttpStatusCode.OK, "User-agent: *\n"), _ => Response(HttpStatusCode.OK, payload));
        using var directoryOwner = new TemporaryDirectory();
        var directory = directoryOwner.Path;
        using var collector = new HttpCollector(handler);
        var result = await collector.FetchAsync(Request(directory));
        Assert.Equal(CollectionOutcome.Captured, result.Outcome);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant(), result.Capture!.Sha256);
        Assert.Equal(2, result.RequestCount);
        await using var file = File.OpenRead(Path.Combine(directory, result.Capture!.RelativePath));
        await using var gzip = new GZipStream(file, CompressionMode.Decompress);
        using var output = new MemoryStream(); await gzip.CopyToAsync(output);
        Assert.Equal(payload, output.ToArray());
    }

    [Fact]
    public async Task FeedModeParsesScopedRssLinksAndKeepsRawCapture()
    {
        using var directory = new TemporaryDirectory();
        var feed = "<rss version='2.0'><channel><item><link>/watch/story</link></item><item><link>https://example.test/watch/story</link></item></channel></rss>";
        var payload = Encoding.UTF8.GetBytes(feed);
        using var collector = new HttpCollector(new QueueHandler(_ => Response(HttpStatusCode.NotFound), _ => Response(HttpStatusCode.OK, payload)));
        var result = await collector.FetchAsync(Request(directory.Path) with { Mode = CollectionMode.Feed });
        Assert.Equal(CollectionOutcome.Captured, result.Outcome);
        Assert.Equal(FeedDiscoveryStatus.Parsed, result.Discovery!.Status);
        Assert.Equal(new[] { "https://example.test/watch/story" }, result.Discovery.Urls);
        await using var file = File.OpenRead(Path.Combine(directory.Path, result.Capture!.RelativePath));
        await using var gzip = new GZipStream(file, CompressionMode.Decompress);
        using var output = new MemoryStream(); await gzip.CopyToAsync(output);
        Assert.Equal(payload, output.ToArray());
    }

    [Fact]
    public async Task OutOfScopeFeedLinksAreFilteredAndCaptureIsRetained()
    {
        using var directory = new TemporaryDirectory();
        var payload = Encoding.UTF8.GetBytes("<rss version='2.0'><channel><item><link>https://outside.test/watch/x</link></item></channel></rss>");
        using var collector = new HttpCollector(new QueueHandler(_ => Response(HttpStatusCode.NotFound), _ => Response(HttpStatusCode.OK, payload)));
        var result = await collector.FetchAsync(Request(directory.Path) with { Mode = CollectionMode.Feed });
        Assert.Equal(CollectionOutcome.Captured, result.Outcome);
        Assert.NotNull(result.Capture);
        Assert.Equal(FeedDiscoveryStatus.Parsed, result.Discovery!.Status);
        Assert.Empty(result.Discovery.Urls);
    }

    [Fact]
    public async Task FeedRelativeLinksUseRedirectedResponseUrlAndXmlBaseIsUnsupported()
    {
        using var directory = new TemporaryDirectory();
        var redirectedFeed = Encoding.UTF8.GetBytes("<rss version='2.0'><channel><item><link>story</link></item></channel></rss>");
        var handler = new QueueHandler(_ => Response(HttpStatusCode.NotFound), _ => Redirect("https://example.test/watch/feeds/current.xml"),
            _ => Response(HttpStatusCode.OK, redirectedFeed));
        using var collector = new HttpCollector(handler);
        var result = await collector.FetchAsync(Request(directory.Path) with { Mode = CollectionMode.Feed });
        Assert.Equal(new[] { "https://example.test/watch/feeds/story" }, result.Discovery!.Urls);

        using var nextDirectory = new TemporaryDirectory();
        var xmlBase = Encoding.UTF8.GetBytes("<feed xmlns='http://www.w3.org/2005/Atom' xml:base='https://example.test/watch/'><entry><link href='story'/></entry></feed>");
        using var nextCollector = new HttpCollector(new QueueHandler(_ => Response(HttpStatusCode.NotFound), _ => Response(HttpStatusCode.OK, xmlBase)));
        var unsupported = await nextCollector.FetchAsync(Request(nextDirectory.Path) with { Mode = CollectionMode.Feed });
        Assert.Equal(FeedDiscoveryStatus.Unsupported, unsupported.Discovery!.Status);
        Assert.Empty(unsupported.Discovery.Urls);
        Assert.NotNull(unsupported.Capture);
    }

    [Fact]
    public async Task FeedCandidateLimitReturnsNoPartialCandidates()
    {
        using var directory = new TemporaryDirectory();
        var payload = Encoding.UTF8.GetBytes("<feed xmlns='http://www.w3.org/2005/Atom'><entry><link href='/watch/a'/></entry><entry><link href='/watch/b'/></entry></feed>");
        using var collector = new HttpCollector(new QueueHandler(_ => Response(HttpStatusCode.NotFound), _ => Response(HttpStatusCode.OK, payload)));
        var result = await collector.FetchAsync(Request(directory.Path) with { Mode = CollectionMode.Feed, MaxCandidates = 1 });
        Assert.Equal(FeedDiscoveryStatus.LimitExceeded, result.Discovery!.Status);
        Assert.Empty(result.Discovery.Urls);
    }

    [Fact]
    public async Task FeedXmlLimitsDepthAndRejectsForeignRootNamespace()
    {
        using var directory = new TemporaryDirectory();
        var deepXml = Encoding.UTF8.GetBytes("<rss version='2.0'><channel>" + string.Concat(Enumerable.Repeat("<x>", 66)) +
            string.Concat(Enumerable.Repeat("</x>", 66)) + "</channel></rss>");
        using var collector = new HttpCollector(new QueueHandler(_ => Response(HttpStatusCode.NotFound), _ => Response(HttpStatusCode.OK, deepXml)));
        var limited = await collector.FetchAsync(Request(directory.Path) with { Mode = CollectionMode.Feed });
        Assert.Equal(FeedDiscoveryStatus.LimitExceeded, limited.Discovery!.Status);
        Assert.Empty(limited.Discovery.Urls);

        using var foreignDirectory = new TemporaryDirectory();
        var foreign = Encoding.UTF8.GetBytes("<rss xmlns='urn:foreign' version='2.0'><channel><item><link>/watch/a</link></item></channel></rss>");
        using var foreignCollector = new HttpCollector(new QueueHandler(_ => Response(HttpStatusCode.NotFound), _ => Response(HttpStatusCode.OK, foreign)));
        var unsupported = await foreignCollector.FetchAsync(Request(foreignDirectory.Path) with { Mode = CollectionMode.Feed });
        Assert.Equal(FeedDiscoveryStatus.Unsupported, unsupported.Discovery!.Status);
        Assert.Empty(unsupported.Discovery.Urls);
    }

    [Theory]
    [InlineData("iso-8859-1", false, "caf%C3%83%C2%A9")]
    [InlineData("iso-8859-1", true, "caf%C3%A9")]
    [InlineData("utf-8", false, "caf%C3%A9")]
    public async Task FeedCharsetUsesBomThenHttpHeaderThenXmlDeclaration(string charset, bool bom, string expectedPath)
    {
        using var directory = new TemporaryDirectory();
        var xml = "<?xml version='1.0' encoding='iso-8859-1'?><rss version='2.0'><channel><item><link>/watch/café</link></item></channel></rss>";
        var body = Encoding.UTF8.GetBytes(xml);
        var payload = bom ? Encoding.UTF8.GetPreamble().Concat(body).ToArray() : body;
        using var collector = new HttpCollector(new QueueHandler(_ => Response(HttpStatusCode.NotFound), _ =>
        {
            var response = Response(HttpStatusCode.OK, payload);
            response.Content.Headers.TryAddWithoutValidation("Content-Type", "application/rss+xml; charset=" + charset);
            return response;
        }));
        var result = await collector.FetchAsync(Request(directory.Path) with { Mode = CollectionMode.Feed });
        Assert.Equal(CollectionOutcome.Captured, result.Outcome);
        Assert.Equal(FeedDiscoveryStatus.Parsed, result.Discovery!.Status);
        Assert.Equal("https://example.test/watch/" + expectedPath, Assert.Single(result.Discovery.Urls));
        Assert.Equal(Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant(), result.Capture!.Sha256);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FeedConsumesExactlyOneUtf16ByteOrderMark(bool duplicate)
    {
        using var directory = new TemporaryDirectory();
        var xml = "<rss version='2.0'><channel><item><link>/watch/article</link></item></channel></rss>";
        var preamble = Encoding.Unicode.GetPreamble();
        var prefix = duplicate ? preamble.Concat(preamble) : preamble;
        var payload = prefix.Concat(Encoding.Unicode.GetBytes(xml)).ToArray();
        using var collector = new HttpCollector(new QueueHandler(_ => Response(HttpStatusCode.NotFound), _ => Response(HttpStatusCode.OK, payload)));
        var result = await collector.FetchAsync(Request(directory.Path) with { Mode = CollectionMode.Feed });
        Assert.Equal(CollectionOutcome.Captured, result.Outcome);
        Assert.Equal(duplicate ? FeedDiscoveryStatus.Invalid : FeedDiscoveryStatus.Parsed, result.Discovery!.Status);
        if (duplicate) Assert.Empty(result.Discovery.Urls);
        else Assert.Equal("https://example.test/watch/article", Assert.Single(result.Discovery.Urls));
    }

    [Theory]
    [InlineData("unknown-charset", FeedDiscoveryStatus.Unsupported)]
    [InlineData("utf-8", FeedDiscoveryStatus.Invalid)]
    public async Task UndecodableFeedKeepsCaptureWithoutCandidates(string charset, FeedDiscoveryStatus status)
    {
        using var directory = new TemporaryDirectory();
        var payload = Encoding.Latin1.GetBytes("<rss version='2.0'><channel><item><link>/watch/café</link></item></channel></rss>");
        using var collector = new HttpCollector(new QueueHandler(_ => Response(HttpStatusCode.NotFound), _ =>
        {
            var response = Response(HttpStatusCode.OK, payload);
            response.Content.Headers.TryAddWithoutValidation("Content-Type", "application/rss+xml; charset=" + charset);
            return response;
        }));
        var result = await collector.FetchAsync(Request(directory.Path) with { Mode = CollectionMode.Feed });
        Assert.Equal(CollectionOutcome.Captured, result.Outcome);
        Assert.NotNull(result.Capture);
        Assert.Equal(status, result.Discovery!.Status);
        Assert.Empty(result.Discovery.Urls);
    }

    [Theory]
    [InlineData("gzip", false)]
    [InlineData("gzip", true)]
    [InlineData("deflate", false)]
    [InlineData("deflate", true)]
    [InlineData("br", false)]
    [InlineData("br", true)]
    public async Task CompressedFeedsRequireCompleteEncoding(string coding, bool truncate)
    {
        using var directory = new TemporaryDirectory();
        var xml = Encoding.UTF8.GetBytes("<rss version='2.0'><channel><item><link>/watch/article</link></item></channel></rss>");
        using var output = new MemoryStream();
        using (Stream encoder = coding switch
        {
            "gzip" => new GZipStream(output, CompressionLevel.NoCompression, leaveOpen: true),
            "deflate" => new ZLibStream(output, CompressionLevel.NoCompression, leaveOpen: true),
            _ => new BrotliStream(output, CompressionLevel.NoCompression, leaveOpen: true)
        })
            encoder.Write(xml);
        var encoded = output.ToArray();
        var payload = truncate ? encoded[..^(coding == "gzip" ? 8 : coding == "deflate" ? 4 : 1)] : encoded;
        using var collector = new HttpCollector(new QueueHandler(_ => Response(HttpStatusCode.NotFound), _ =>
        {
            var response = Response(HttpStatusCode.OK, payload);
            response.Content.Headers.ContentEncoding.Add(coding);
            return response;
        }));
        var result = await collector.FetchAsync(Request(directory.Path) with { Mode = CollectionMode.Feed });
        Assert.Equal(CollectionOutcome.Captured, result.Outcome);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant(), result.Capture!.Sha256);
        Assert.Equal(truncate ? FeedDiscoveryStatus.Invalid : FeedDiscoveryStatus.Parsed, result.Discovery!.Status);
        if (truncate) Assert.Empty(result.Discovery.Urls);
        else Assert.Equal("https://example.test/watch/article", Assert.Single(result.Discovery.Urls));
    }

    [Fact]
    public async Task NestedRssLinkMarkupCannotInventCandidateUrls()
    {
        using var directory = new TemporaryDirectory();
        var xml = "<rss version='2.0'><channel><item><link>/watch/a<x>b</x>c</link></item></channel></rss>";
        using var collector = new HttpCollector(new QueueHandler(_ => Response(HttpStatusCode.NotFound), _ => Response(HttpStatusCode.OK, xml)));
        var result = await collector.FetchAsync(Request(directory.Path) with { Mode = CollectionMode.Feed });
        Assert.Equal(CollectionOutcome.Captured, result.Outcome);
        Assert.Equal(FeedDiscoveryStatus.Invalid, result.Discovery!.Status);
        Assert.Empty(result.Discovery.Urls);
    }

    [Fact]
    public async Task IdenticalBytesCanHaveDifferentRepresentationMetadata()
    {
        using var directory = new TemporaryDirectory();
        var payload = Encoding.UTF8.GetBytes("opaque response body");
        var handler = new QueueHandler(
            _ => Response(HttpStatusCode.NotFound),
            _ =>
            {
                var response = Response(HttpStatusCode.OK, payload);
                response.Content.Headers.TryAddWithoutValidation("Content-Type", "text/plain; charset=iso-8859-1");
                return response;
            },
            _ => Response(HttpStatusCode.NotFound),
            _ =>
            {
                var response = Response(HttpStatusCode.OK, payload);
                response.Content.Headers.TryAddWithoutValidation("Content-Type", "application/octet-stream");
                response.Content.Headers.TryAddWithoutValidation("Content-Encoding", "vendor-opaque, gzip");
                return response;
            });
        using var collector = new HttpCollector(handler);
        var first = await collector.FetchAsync(Request(directory.Path));
        var second = await collector.FetchAsync(Request(directory.Path));

        Assert.Equal(CollectionOutcome.Captured, first.Outcome);
        Assert.Equal(CollectionOutcome.Captured, second.Outcome);
        Assert.Equal(first.Capture, second.Capture);
        Assert.Equal("text/plain; charset=iso-8859-1", first.Response!.ContentType);
        Assert.Empty(first.Response.ContentEncodings);
        Assert.Equal("application/octet-stream", second.Response!.ContentType);
        Assert.Equal(new[] { "vendor-opaque", "gzip" }, second.Response.ContentEncodings);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(payload)), second.Capture!.Sha256);
        Assert.Single(Directory.GetFiles(directory.Path, "*.gz"));
    }

    [Theory]
    [InlineData("Content-Type", "not a media type")]
    [InlineData("Content-Encoding", "bad coding")]
    [InlineData("ETag", "*")]
    [InlineData("Last-Modified", "invalid-date")]
    public async Task MalformedResponseMetadataFailsBeforeWritingCapture(string name, string value)
    {
        using var directory = new TemporaryDirectory();
        var handler = new QueueHandler(_ => Response(HttpStatusCode.NotFound), _ =>
        {
            var response = Response(HttpStatusCode.OK, "body");
            if (name == "ETag") response.Headers.TryAddWithoutValidation(name, value);
            else response.Content.Headers.TryAddWithoutValidation(name, value);
            return response;
        });
        using var collector = new HttpCollector(handler);
        var result = await collector.FetchAsync(Request(directory.Path));
        Assert.Equal(CollectionOutcome.Failed, result.Outcome);
        Assert.Equal(CollectionFailureCode.InvalidResponse, result.FailureCode);
        Assert.Null(result.Capture);
        Assert.Null(result.Response);
        Assert.Empty(Directory.GetFiles(directory.Path));
    }

    [Fact]
    public async Task DuplicateLastModifiedHeadersCannotBeSilentlyDiscarded()
    {
        using var directory = new TemporaryDirectory();
        var handler = new QueueHandler(_ => Response(HttpStatusCode.NotFound), _ =>
        {
            var response = Response(HttpStatusCode.OK, "body");
            response.Content.Headers.TryAddWithoutValidation("Last-Modified",
                new[] { "Tue, 06 Oct 2026 12:00:00 GMT", "invalid-date" });
            return response;
        });
        using var collector = new HttpCollector(handler);
        var result = await collector.FetchAsync(Request(directory.Path));
        Assert.Equal(CollectionFailureCode.InvalidResponse, result.FailureCode);
        Assert.Null(result.Capture);
        Assert.Empty(Directory.GetFiles(directory.Path));
    }

    [Fact]
    public async Task RedirectTransportFailureCannotReusePreviousResponseMetadata()
    {
        using var directory = new TemporaryDirectory();
        var handler = new QueueHandler(_ => Response(HttpStatusCode.NotFound),
            _ => Redirect("https://example.test/watch/next"), _ => throw new HttpRequestException("Fixture transport failure"));
        using var collector = new HttpCollector(handler);
        var result = await collector.FetchAsync(Request(directory.Path));
        Assert.Equal(CollectionFailureCode.TransportError, result.FailureCode);
        Assert.Equal("https://example.test/watch/next", result.FinalUrl);
        Assert.Null(result.Response);
        Assert.Equal(3, result.RequestCount);
    }

    [Fact]
    public async Task BomPrefixedRobotsDenyPreventsTargetRequest()
    {
        var handler = new QueueHandler(_ => Response(HttpStatusCode.OK, "\uFEFFUser-agent: *\nDisallow: /watch\n"));
        using var collector = new HttpCollector(handler);
        using var directoryOwner = new TemporaryDirectory();
        var result = await collector.FetchAsync(Request(directoryOwner.Path));
        Assert.Equal(CollectionFailureCode.RobotsDenied, result.FailureCode);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task RobotsDenyPreventsTargetRequest()
    {
        using var directoryOwner = new TemporaryDirectory();
        var handler = new QueueHandler(_ => Response(HttpStatusCode.OK, "User-agent: *\nDisallow: /watch*\n"));
        using var collector = new HttpCollector(handler);
        var result = await collector.FetchAsync(Request(directoryOwner.Path));
        Assert.Equal(CollectionFailureCode.RobotsDenied, result.FailureCode);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task RobotsRulesMatchPercentEncodedUnreservedPathCharacters()
    {
        using var directoryOwner = new TemporaryDirectory();
        var handler = new QueueHandler(_ => Response(HttpStatusCode.OK, "User-agent: *\nDisallow: /watch/secret\n"));
        using var collector = new HttpCollector(handler);
        var result = await collector.FetchAsync(Request(directoryOwner.Path) with { Url = "https://example.test/watch/%73ecret" });
        Assert.Equal(CollectionFailureCode.RobotsDenied, result.FailureCode);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task RobotsUnicodeRuleMatchesPercentEncodedUrl()
    {
        using var directoryOwner = new TemporaryDirectory();
        var handler = new QueueHandler(_ => Response(HttpStatusCode.OK, "User-agent: *\nDisallow: /watch/café\n"));
        using var collector = new HttpCollector(handler);
        var result = await collector.FetchAsync(Request(directoryOwner.Path) with { Url = "https://example.test/watch/caf%C3%A9" });
        Assert.Equal(CollectionFailureCode.RobotsDenied, result.FailureCode);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task OverlongRobotsRuleFailsClosed()
    {
        using var directoryOwner = new TemporaryDirectory();
        var robots = "User-agent: *\nDisallow: /" + new string('x', 10_000) + "\n";
        var handler = new QueueHandler(_ => Response(HttpStatusCode.OK, robots));
        using var collector = new HttpCollector(handler);
        var result = await collector.FetchAsync(Request(directoryOwner.Path) with { MaxBytes = 20_000 });
        Assert.Equal(CollectionFailureCode.RobotsUnavailable, result.FailureCode);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task EncodedRobotsFileFailsClosed()
    {
        using var directoryOwner = new TemporaryDirectory();
        var handler = new QueueHandler(_ =>
        {
            var response = Response(HttpStatusCode.OK, "User-agent: *\nDisallow: /\n");
            response.Content.Headers.ContentEncoding.Add("gzip");
            return response;
        });
        using var collector = new HttpCollector(handler);
        var result = await collector.FetchAsync(Request(directoryOwner.Path));
        Assert.Equal(CollectionFailureCode.RobotsUnavailable, result.FailureCode);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task RejectsOutOfScopeRedirectBeforeSecondRequest()
    {
        using var directoryOwner = new TemporaryDirectory();
        var handler = new QueueHandler(_ => Response(HttpStatusCode.NotFound), _ => Redirect("https://outside.test/watch"));
        using var collector = new HttpCollector(handler);
        var result = await collector.FetchAsync(Request(directoryOwner.Path));
        Assert.Equal(CollectionFailureCode.OutOfScope, result.FailureCode);
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task ReceiptRetainsSerializedConditionalHeaders()
    {
        using var directory = new TemporaryDirectory();
        var modified = new DateTimeOffset(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);
        var handler = new QueueHandler(_ => Response(HttpStatusCode.NotFound), message =>
        {
            Assert.Equal("W/\"v1\"", message.Headers.GetValues("If-None-Match").Single());
            Assert.Equal("Tue, 06 Oct 2026 12:00:00 GMT", message.Headers.GetValues("If-Modified-Since").Single());
            return Response(HttpStatusCode.NotModified);
        });
        using var collector = new HttpCollector(handler);

        var result = await collector.FetchAsync(Request(directory.Path) with
        {
            ETag = "  W/\"v1\"  ",
            LastModified = modified.AddTicks(1234567).ToOffset(TimeSpan.FromHours(2))
        });

        Assert.Equal(CollectionOutcome.NotModified, result.Outcome);
        Assert.Equal(new HttpRequestValidators { ETag = "W/\"v1\"", LastModified = modified }, result.SentValidators);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RobotsFailuresHaveNoSentValidators(bool denied)
    {
        using var directory = new TemporaryDirectory();
        var handler = new QueueHandler(message =>
        {
            Assert.Empty(message.Headers.IfNoneMatch);
            Assert.Null(message.Headers.IfModifiedSince);
            return denied ? Response(HttpStatusCode.OK, "User-agent: *\nDisallow: /watch\n") :
                Response(HttpStatusCode.ServiceUnavailable);
        });
        using var collector = new HttpCollector(handler);

        var result = await collector.FetchAsync(Request(directory.Path) with { ETag = "\"v1\"" });

        Assert.Equal(denied ? CollectionFailureCode.RobotsDenied : CollectionFailureCode.RobotsUnavailable, result.FailureCode);
        Assert.Null(result.SentValidators);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task RedirectFailureCannotInheritOriginalRequestValidators()
    {
        using var directory = new TemporaryDirectory();
        var handler = new QueueHandler(_ => Response(HttpStatusCode.NotFound),
            _ => Redirect("https://example.test/watch/next"), message =>
            {
                Assert.Empty(message.Headers.IfNoneMatch);
                Assert.Null(message.Headers.IfModifiedSince);
                throw new HttpRequestException("Fixture transport failure");
            });
        using var collector = new HttpCollector(handler);

        var result = await collector.FetchAsync(Request(directory.Path) with { ETag = "\"v1\"" });

        Assert.Equal(CollectionFailureCode.TransportError, result.FailureCode);
        Assert.Equal("https://example.test/watch/next", result.FinalUrl);
        Assert.Null(result.SentValidators);
    }

    [Fact]
    public async Task HandlesConditionalNotModifiedAndRateLimitWithoutRetry()
    {
        var notModified = new QueueHandler(_ => Response(HttpStatusCode.NotFound), _ => Response(HttpStatusCode.NotModified));
        using (var collector = new HttpCollector(notModified))
        {
            using var directoryOwner = new TemporaryDirectory();
            var result = await collector.FetchAsync(Request(directoryOwner.Path) with { ETag = "\"v1\"" });
            Assert.Equal(CollectionOutcome.NotModified, result.Outcome);
        }
        using var throttledDirectory = new TemporaryDirectory();
        var throttled = new QueueHandler(_ => Response(HttpStatusCode.NotFound), _ =>
        {
            var response = new HttpResponseMessage((HttpStatusCode)429); response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(23)); return response;
        });
        using (var collector = new HttpCollector(throttled))
        {
            var result = await collector.FetchAsync(Request(throttledDirectory.Path));
            Assert.Equal(CollectionOutcome.Deferred, result.Outcome);
            Assert.Equal(23, result.RetryAfterSeconds);
            Assert.Equal(2, throttled.Calls);
        }
    }

    [Fact]
    public async Task OversizedBodyReturnsFailureWithoutArtifact()
    {
        using var directoryOwner = new TemporaryDirectory();
        var directory = directoryOwner.Path;
        var handler = new QueueHandler(_ => Response(HttpStatusCode.NotFound), _ => Response(HttpStatusCode.OK, new byte[20]));
        using var collector = new HttpCollector(handler);
        var result = await collector.FetchAsync(Request(directory) with { MaxBytes = 10 });
        Assert.Equal(CollectionFailureCode.Oversized, result.FailureCode);
        Assert.Empty(Directory.GetFiles(directory));
    }

    [Fact]
    public async Task TruncatedBodyReturnsFailureWithoutArtifact()
    {
        using var directoryOwner = new TemporaryDirectory();
        var directory = directoryOwner.Path;
        var handler = new QueueHandler(_ => Response(HttpStatusCode.NotFound), _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new TruncatedContent() });
        using var collector = new HttpCollector(handler);
        var result = await collector.FetchAsync(Request(directory));
        Assert.Equal(CollectionFailureCode.IncompleteResponse, result.FailureCode);
        Assert.Empty(Directory.GetFiles(directory));
    }

    [Fact]
    public async Task CancellationDuringBodyLeavesNoPartialCapture()
    {
        using var directoryOwner = new TemporaryDirectory();
        var directory = directoryOwner.Path; using var cts = new CancellationTokenSource();
        var handler = new QueueHandler(_ => Response(HttpStatusCode.NotFound), _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new CancelContent(cts) });
        using var collector = new HttpCollector(handler);
        var result = await collector.FetchAsync(Request(directory), cts.Token);
        Assert.Equal(CollectionFailureCode.Cancelled, result.FailureCode);
        Assert.Empty(Directory.GetFiles(directory));
    }

    private static CollectionRequest Request(string directory) => new()
    {
        JobId = "job",
        SourceId = "source",
        Url = "https://example.test/watch",
        AllowedOrigin = "https://example.test",
        AllowedPathPrefix = "/watch",
        ArtifactDirectory = directory,
        MaxRequests = 5,
        MaxBytes = 1000,
        MinDelayMilliseconds = 0
    };
    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "collector-" + Guid.NewGuid().ToString("N"));
        public TemporaryDirectory() => Directory.CreateDirectory(Path);
        public void Dispose() { if (Directory.Exists(Path)) Directory.Delete(Path, true); }
    }
    private static HttpResponseMessage Response(HttpStatusCode status, string text = "") => Response(status, Encoding.UTF8.GetBytes(text));
    private static HttpResponseMessage Response(HttpStatusCode status, byte[] bytes) => new(status) { Content = new ByteArrayContent(bytes) };
    private static HttpResponseMessage Redirect(string uri) { var response = new HttpResponseMessage(HttpStatusCode.Found); response.Headers.Location = new Uri(uri); return response; }

    private sealed class QueueHandler(params Func<HttpRequestMessage, HttpResponseMessage>[] responses) : HttpMessageHandler
    {
        private int index;
        public int Calls => index;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = responses[index++](request);
            return Task.FromResult(response);
        }
    }

    private sealed class TruncatedContent : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => Task.CompletedTask;
        protected override bool TryComputeLength(out long length) { length = 100; return true; }
        protected override Task<Stream> CreateContentReadStreamAsync() => Task.FromResult<Stream>(new MemoryStream(new byte[10]));
    }
    private sealed class CancelContent(CancellationTokenSource source) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => Task.CompletedTask;
        protected override bool TryComputeLength(out long length) { length = -1; return false; }
        protected override Task<Stream> CreateContentReadStreamAsync() => Task.FromResult<Stream>(new CancellingStream(source));
    }
    private sealed class CancellingStream(CancellationTokenSource source) : MemoryStream(new byte[100])
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            source.Cancel(); return ValueTask.FromCanceled<int>(cancellationToken);
        }
    }
}
