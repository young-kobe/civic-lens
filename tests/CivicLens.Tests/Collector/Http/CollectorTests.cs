using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using CivicLens.Collection.Contracts;
using CivicLens.Collector.Http;

namespace CivicLens.Tests.Collector.Http;

public sealed class CollectorTests
{
    [Theory]
    [InlineData(CollectionMode.Feed, "deflate", false, 0)]
    [InlineData(CollectionMode.Feed, "deflate", true, 0)]
    [InlineData(CollectionMode.Feed, "br", false, 0)]
    [InlineData(CollectionMode.Feed, "br", true, 0)]
    [InlineData(CollectionMode.Html, "deflate", false, 0)]
    [InlineData(CollectionMode.Html, "deflate", true, 0)]
    [InlineData(CollectionMode.Html, "br", false, 0)]
    [InlineData(CollectionMode.Html, "br", true, 0)]
    [InlineData(CollectionMode.Feed, "br", false, 10_000_001)]
    [InlineData(CollectionMode.Html, "br", false, 10_000_001)]
    public async Task NestedDiscoveryEncodingValidatesEveryLayer(CollectionMode mode, string innerCoding, bool truncate, int padding)
    {
        using var directory = new TemporaryDirectory();
        var markup = mode == CollectionMode.Feed
            ? "<rss version='2.0'><channel><item><link>/watch/article</link></item></channel></rss>"
            : "<a href='/watch/article'>article</a>";
        using var inner = new MemoryStream();
        using (Stream encoder = innerCoding == "br"
            ? new BrotliStream(inner, CompressionLevel.SmallestSize, leaveOpen: true)
            : new ZLibStream(inner, CompressionLevel.SmallestSize, leaveOpen: true))
            encoder.Write(Encoding.UTF8.GetBytes(markup));
        using var outer = new MemoryStream();
        using (var encoder = new GZipStream(outer, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            encoder.Write(inner.ToArray());
            if (padding > 0) encoder.Write(new byte[padding]);
        }
        var payload = truncate ? outer.ToArray()[..^4] : outer.ToArray();
        using var collector = new HttpCollector(new QueueHandler(_ => Response(HttpStatusCode.NotFound), _ =>
        {
            var response = Response(HttpStatusCode.OK, payload);
            response.Content.Headers.ContentEncoding.Add(innerCoding);
            response.Content.Headers.ContentEncoding.Add("gzip");
            return response;
        }));
        var result = await collector.FetchAsync(Request(directory.Path) with { Mode = mode, MaxBytes = 100_000 });
        Assert.Equal(CollectionOutcome.Captured, result.Outcome);
        Assert.NotNull(result.Capture);
        var expected = padding > 0 ? DiscoveryStatus.LimitExceeded : truncate ? DiscoveryStatus.Invalid : DiscoveryStatus.Parsed;
        Assert.Equal(expected, result.Discovery!.Status);
        if (expected != DiscoveryStatus.Parsed) Assert.Empty(result.Discovery.Urls);
        else Assert.Equal("https://example.test/watch/article", Assert.Single(result.Discovery.Urls));
    }

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
        Assert.Equal(DiscoveryStatus.Parsed, result.Discovery!.Status);
        Assert.Equal(new[] { "https://example.test/watch/story" }, result.Discovery.Urls);
        await using var file = File.OpenRead(Path.Combine(directory.Path, result.Capture!.RelativePath));
        await using var gzip = new GZipStream(file, CompressionMode.Decompress);
        using var output = new MemoryStream(); await gzip.CopyToAsync(output);
        Assert.Equal(payload, output.ToArray());
    }

    [Fact]
    public async Task VersionFourFeedRequestKeepsItsProtocolVersionAndDiscovery()
    {
        using var directory = new TemporaryDirectory();
        var payload = Encoding.UTF8.GetBytes("<rss version='2.0'><channel><item><link>/watch/story</link></item></channel></rss>");
        using var collector = new HttpCollector(new QueueHandler(_ => Response(HttpStatusCode.NotFound), _ => Response(HttpStatusCode.OK, payload)));
        var result = await collector.FetchAsync(Request(directory.Path) with
        {
            Version = CollectionProtocol.PreviousVersion,
            Mode = CollectionMode.Feed
        });

        Assert.Equal(CollectionOutcome.Captured, result.Outcome);
        Assert.Equal(CollectionProtocol.PreviousVersion, result.Version);
        Assert.Equal(DiscoveryStatus.Parsed, result.Discovery!.Status);
        Assert.Equal("https://example.test/watch/story", Assert.Single(result.Discovery.Urls));
    }

    [Fact]
    public async Task HtmlModeUsesFirstValidBasePreservesQueriesStripsFragmentsAndDeduplicatesInOrder()
    {
        using var directory = new TemporaryDirectory();
        var html = "<html><head><base href='http://[invalid'><base href='articles/'></head>" +
            "<body><a href='one?edition=2#top'>one</a><a href='one?edition=2#other'>duplicate</a>" +
            "<a href='/watch/two?x=1&amp;y=2#frag'>two</a><a href='https://outside.test/watch/no'>outside</a></body></html>";
        using var collector = new HttpCollector(new QueueHandler(_ => Response(HttpStatusCode.NotFound), _ => Response(HttpStatusCode.OK, html)));
        var result = await collector.FetchAsync(Request(directory.Path) with
        {
            Url = "https://example.test/watch/index.html",
            Mode = CollectionMode.Html,
            MaxBytes = 100_000
        });

        Assert.Equal(CollectionOutcome.Captured, result.Outcome);
        Assert.NotNull(result.Capture);
        Assert.Equal(DiscoveryStatus.Parsed, result.Discovery!.Status);
        Assert.Equal(new[]
        {
            "https://example.test/watch/articles/one?edition=2",
            "https://example.test/watch/two?x=1&y=2"
        }, result.Discovery.Urls);
    }

    [Fact]
    public async Task HtmlParserToleratesMalformedMarkupAndCandidateLimitDoesNotReturnPartialUrls()
    {
        using var directory = new TemporaryDirectory();
        var html = "<a href='/watch/first'><b>first<a href='/watch/second'>second";
        using var collector = new HttpCollector(new QueueHandler(_ => Response(HttpStatusCode.NotFound), _ => Response(HttpStatusCode.OK, html)));
        var result = await collector.FetchAsync(Request(directory.Path) with { Mode = CollectionMode.Html, MaxCandidates = 1 });
        Assert.Equal(CollectionOutcome.Captured, result.Outcome);
        Assert.NotNull(result.Capture);
        Assert.Equal(DiscoveryStatus.LimitExceeded, result.Discovery!.Status);
        Assert.Empty(result.Discovery.Urls);
    }

    [Fact]
    public async Task HtmlDiscoveryRejectsUnknownEncodingWithoutLosingCapture()
    {
        using var directory = new TemporaryDirectory();
        using var collector = new HttpCollector(new QueueHandler(_ => Response(HttpStatusCode.NotFound), _ =>
        {
            var response = Response(HttpStatusCode.OK, "<a href='/watch/story'>story</a>");
            response.Content.Headers.ContentEncoding.Add("made-up");
            return response;
        }));
        var result = await collector.FetchAsync(Request(directory.Path) with { Mode = CollectionMode.Html });
        Assert.Equal(CollectionOutcome.Captured, result.Outcome);
        Assert.NotNull(result.Capture);
        Assert.Equal(DiscoveryStatus.Unsupported, result.Discovery!.Status);
        Assert.Empty(result.Discovery.Urls);
    }

    [Fact]
    public async Task HtmlDiscoveryDecodesCompressedRepresentationAndRetainsEncodedCaptureBytes()
    {
        using var directory = new TemporaryDirectory();
        var html = Encoding.UTF8.GetBytes("<a href='/watch/story?edition=1#top'>story</a>");
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
            gzip.Write(html);
        var encoded = output.ToArray();
        using var collector = new HttpCollector(new QueueHandler(_ => Response(HttpStatusCode.NotFound), _ =>
        {
            var response = Response(HttpStatusCode.OK, encoded);
            response.Content.Headers.ContentEncoding.Add("gzip");
            return response;
        }));

        var result = await collector.FetchAsync(Request(directory.Path) with { Mode = CollectionMode.Html });
        Assert.Equal(CollectionOutcome.Captured, result.Outcome);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(encoded)), result.Capture!.Sha256);
        Assert.Equal(DiscoveryStatus.Parsed, result.Discovery!.Status);
        Assert.Equal("https://example.test/watch/story?edition=1", Assert.Single(result.Discovery.Urls));
    }

    [Fact]
    public async Task HtmlDecodedByteLimitRetainsCaptureAndReturnsNoPartialCandidates()
    {
        using var directory = new TemporaryDirectory();
        var html = Encoding.UTF8.GetBytes("<a href='/watch/story'>story</a><p>" + new string('x', 10_000_000) + "</p>");
        using var collector = new HttpCollector(new QueueHandler(_ => Response(HttpStatusCode.NotFound), _ => Response(HttpStatusCode.OK, html)));

        var result = await collector.FetchAsync(Request(directory.Path) with
        {
            Mode = CollectionMode.Html,
            MaxBytes = 20_000_000
        });
        Assert.Equal(CollectionOutcome.Captured, result.Outcome);
        Assert.NotNull(result.Capture);
        Assert.Equal(DiscoveryStatus.LimitExceeded, result.Discovery!.Status);
        Assert.Empty(result.Discovery.Urls);
    }

    [Theory]
    [InlineData("<!-- <meta charset='unknown'> -->")]
    [InlineData("<script>const example = \"<meta charset='unknown'>\";</script>")]
    [InlineData("<meta data-charset='unknown'>")]
    [InlineData("<meta content='text/html; charset=unknown'>")]
    public async Task HtmlEncodingIgnoresDeclarationsOutsideActualCharsetMetadata(string prefix)
    {
        using var directory = new TemporaryDirectory();
        using var collector = new HttpCollector(new QueueHandler(_ => Response(HttpStatusCode.NotFound),
            _ => Response(HttpStatusCode.OK, prefix + "<a href='/watch/story'>story</a>")));
        var result = await collector.FetchAsync(Request(directory.Path) with { Mode = CollectionMode.Html });
        Assert.Equal(DiscoveryStatus.Parsed, result.Discovery!.Status);
        Assert.Equal("https://example.test/watch/story", Assert.Single(result.Discovery.Urls));
    }

    [Theory]
    [InlineData("<meta charset='utf-8'>")]
    [InlineData("<meta content='text/html; charset=utf-8' http-equiv='content-type'>")]
    public async Task HtmlDeclaredEncodingPreservesNonAsciiUrlBytes(string declaration)
    {
        using var directory = new TemporaryDirectory();
        var bytes = Encoding.UTF8.GetBytes(declaration + "<a href='/watch/café'>story</a>");
        using var collector = new HttpCollector(new QueueHandler(_ => Response(HttpStatusCode.NotFound),
            _ => Response(HttpStatusCode.OK, bytes)));
        var result = await collector.FetchAsync(Request(directory.Path) with { Mode = CollectionMode.Html });
        Assert.Equal(DiscoveryStatus.Parsed, result.Discovery!.Status);
        Assert.Equal("https://example.test/watch/caf%C3%A9", Assert.Single(result.Discovery.Urls));
    }

    [Theory]
    [InlineData("iso-8859-1", false)]
    [InlineData("ascii", false)]
    [InlineData("latin1", true)]
    [InlineData("  latin1  ", true)]
    [InlineData("x-user-defined", true)]
    public async Task HtmlLegacyEncodingLabelsUseHtmlMappingForUrlBytes(string label, bool meta)
    {
        using var directory = new TemporaryDirectory();
        var prefix = meta ? $"<meta charset='{label}'>" : "";
        var bytes = Encoding.Latin1.GetBytes(prefix + "<a href='/watch/price-\u0080'>story</a>");
        using var collector = new HttpCollector(new QueueHandler(_ => Response(HttpStatusCode.NotFound), _ =>
        {
            var response = Response(HttpStatusCode.OK, bytes);
            if (!meta) response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/html") { CharSet = label };
            return response;
        }));
        var result = await collector.FetchAsync(Request(directory.Path) with { Mode = CollectionMode.Html });
        Assert.Equal(DiscoveryStatus.Parsed, result.Discovery!.Status);
        Assert.Equal("https://example.test/watch/price-%E2%82%AC", Assert.Single(result.Discovery.Urls));
    }

    [Fact]
    public async Task HttpUserDefinedCharsetIsUnsupportedRatherThanMappedToWrongUrls()
    {
        using var directory = new TemporaryDirectory();
        var bytes = Encoding.Latin1.GetBytes("<a href='/watch/price-\u0080'>story</a>");
        using var collector = new HttpCollector(new QueueHandler(_ => Response(HttpStatusCode.NotFound), _ =>
        {
            var response = Response(HttpStatusCode.OK, bytes);
            response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/html") { CharSet = "x-user-defined" };
            return response;
        }));
        var result = await collector.FetchAsync(Request(directory.Path) with { Mode = CollectionMode.Html });
        Assert.Equal(CollectionOutcome.Captured, result.Outcome);
        Assert.NotNull(result.Capture);
        Assert.Equal(DiscoveryStatus.Unsupported, result.Discovery!.Status);
        Assert.Empty(result.Discovery.Urls);
    }

    [Theory]
    [InlineData("iso-2022-cn", false)]
    [InlineData("iso-2022-cn-ext", true)]
    [InlineData("iso-2022-kr", false)]
    [InlineData("csiso2022kr", true)]
    [InlineData("hz-gb-2312", false)]
    public async Task HtmlReplacementEncodingLabelsCannotInventCandidates(string charset, bool meta)
    {
        using var directory = new TemporaryDirectory();
        var prefix = meta ? $"<meta charset='{charset}'>" : "";
        using var collector = new HttpCollector(new QueueHandler(_ => Response(HttpStatusCode.NotFound), _ =>
        {
            var response = Response(HttpStatusCode.OK, prefix + "<a href='/watch/fake'>fake</a>");
            if (!meta) response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/html") { CharSet = charset };
            return response;
        }));
        var result = await collector.FetchAsync(Request(directory.Path) with { Mode = CollectionMode.Html });
        Assert.Equal(CollectionOutcome.Captured, result.Outcome);
        Assert.Equal(DiscoveryStatus.Unsupported, result.Discovery!.Status);
        Assert.Empty(result.Discovery.Urls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HtmlUtf32BomIsUnsupportedRatherThanInterpretedAsHtml(bool bigEndian)
    {
        using var directory = new TemporaryDirectory();
        var encoding = new UTF32Encoding(bigEndian, true);
        var bytes = encoding.GetPreamble().Concat(encoding.GetBytes("<a href='/watch/fake'>fake</a>")).ToArray();
        using var collector = new HttpCollector(new QueueHandler(_ => Response(HttpStatusCode.NotFound),
            _ => Response(HttpStatusCode.OK, bytes)));
        var result = await collector.FetchAsync(Request(directory.Path) with { Mode = CollectionMode.Html });
        Assert.Equal(CollectionOutcome.Captured, result.Outcome);
        Assert.Equal(DiscoveryStatus.Unsupported, result.Discovery!.Status);
        Assert.Empty(result.Discovery.Urls);
    }

    [Fact]
    public async Task ForeignNamespaceElementsCannotForgeHtmlBaseOrAnchorLinks()
    {
        using var directory = new TemporaryDirectory();
        var html = "<svg><base href='/watch/forged/'/><a href='/watch/svg'>svg</a></svg>" +
            "<math><a href='/watch/math'>math</a></math><a href='story'>story</a>";
        using var collector = new HttpCollector(new QueueHandler(_ => Response(HttpStatusCode.NotFound),
            _ => Response(HttpStatusCode.OK, html)));
        var result = await collector.FetchAsync(Request(directory.Path) with
        {
            Mode = CollectionMode.Html,
            Url = "https://example.test/watch/index.html"
        });
        Assert.Equal(DiscoveryStatus.Parsed, result.Discovery!.Status);
        Assert.Equal("https://example.test/watch/story", Assert.Single(result.Discovery.Urls));
    }

    [Theory]
    [InlineData("<div>")]
    [InlineData("<b><div></b>")]
    public async Task HtmlExcessiveNestingRetainsCaptureWithoutCandidates(string nesting)
    {
        using var directory = new TemporaryDirectory();
        var html = "<a href='/watch/story'>story</a>" + string.Concat(Enumerable.Repeat(nesting, 1000));
        using var collector = new HttpCollector(new QueueHandler(_ => Response(HttpStatusCode.NotFound),
            _ => Response(HttpStatusCode.OK, html)));
        var result = await collector.FetchAsync(Request(directory.Path) with { Mode = CollectionMode.Html, MaxBytes = 100_000 });
        Assert.Equal(CollectionOutcome.Captured, result.Outcome);
        Assert.NotNull(result.Capture);
        Assert.Equal(DiscoveryStatus.LimitExceeded, result.Discovery!.Status);
        Assert.Empty(result.Discovery.Urls);
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
        Assert.Equal(DiscoveryStatus.Parsed, result.Discovery!.Status);
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
        Assert.Equal(DiscoveryStatus.Unsupported, unsupported.Discovery!.Status);
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
        Assert.Equal(DiscoveryStatus.LimitExceeded, result.Discovery!.Status);
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
        Assert.Equal(DiscoveryStatus.LimitExceeded, limited.Discovery!.Status);
        Assert.Empty(limited.Discovery.Urls);

        using var foreignDirectory = new TemporaryDirectory();
        var foreign = Encoding.UTF8.GetBytes("<rss xmlns='urn:foreign' version='2.0'><channel><item><link>/watch/a</link></item></channel></rss>");
        using var foreignCollector = new HttpCollector(new QueueHandler(_ => Response(HttpStatusCode.NotFound), _ => Response(HttpStatusCode.OK, foreign)));
        var unsupported = await foreignCollector.FetchAsync(Request(foreignDirectory.Path) with { Mode = CollectionMode.Feed });
        Assert.Equal(DiscoveryStatus.Unsupported, unsupported.Discovery!.Status);
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
        Assert.Equal(DiscoveryStatus.Parsed, result.Discovery!.Status);
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
        Assert.Equal(duplicate ? DiscoveryStatus.Invalid : DiscoveryStatus.Parsed, result.Discovery!.Status);
        if (duplicate) Assert.Empty(result.Discovery.Urls);
        else Assert.Equal("https://example.test/watch/article", Assert.Single(result.Discovery.Urls));
    }

    [Theory]
    [InlineData("unknown-charset", DiscoveryStatus.Unsupported)]
    [InlineData("utf-8", DiscoveryStatus.Invalid)]
    public async Task UndecodableFeedKeepsCaptureWithoutCandidates(string charset, DiscoveryStatus status)
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
        Assert.Equal(truncate ? DiscoveryStatus.Invalid : DiscoveryStatus.Parsed, result.Discovery!.Status);
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
        Assert.Equal(DiscoveryStatus.Invalid, result.Discovery!.Status);
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

    [Theory]
    [InlineData("/watch/file-%2A.html", "https://example.test/watch/file-*.html")]
    [InlineData("/watch/foo-%24", "https://example.test/watch/foo-$")]
    public async Task EscapedRobotsSpecialCharactersMatchLiteralUrlCharacters(string rule, string url)
    {
        using var directory = new TemporaryDirectory();
        var handler = new QueueHandler(_ => Response(HttpStatusCode.OK, $"User-agent: *\nDisallow: {rule}\n"),
            _ => Response(HttpStatusCode.OK, "unexpected content"));
        using var collector = new HttpCollector(handler);
        var result = await collector.FetchAsync(Request(directory.Path) with { Url = url });
        Assert.Equal(CollectionFailureCode.RobotsDenied, result.FailureCode);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task CrawlDelayDoesNotSplitConsecutiveUserAgentLines()
    {
        using var directory = new TemporaryDirectory();
        var handler = new QueueHandler(_ => Response(HttpStatusCode.OK,
            "User-agent: CivicLens\nCrawl-delay: 0\nUser-agent: OtherBot\nDisallow: /watch\n"),
            _ => Response(HttpStatusCode.OK, "unexpected content"));
        using var collector = new HttpCollector(handler);
        var result = await collector.FetchAsync(Request(directory.Path));
        Assert.Equal(CollectionFailureCode.RobotsDenied, result.FailureCode);
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData("922337203685", CollectionFailureCode.CrawlDelay)]
    [InlineData("922337203685.001", CollectionFailureCode.RobotsUnavailable)]
    public async Task MaximumCrawlDelayFitsWholeSecondRetryRepresentation(string seconds, CollectionFailureCode expected)
    {
        using var directory = new TemporaryDirectory();
        var handler = new QueueHandler(_ => Response(HttpStatusCode.OK, $"User-agent: *\nCrawl-delay: {seconds}\n"));
        using var collector = new HttpCollector(handler);
        var result = await collector.FetchAsync(Request(directory.Path));
        Assert.Equal(expected, result.FailureCode);
        if (expected == CollectionFailureCode.CrawlDelay)
        {
            Assert.Equal(CollectionProtocol.MaximumCrawlDelayMilliseconds, result.RobotsCrawlDelayMilliseconds);
            Assert.Equal(TimeSpan.MaxValue.Ticks / TimeSpan.TicksPerSecond, result.RetryAfterSeconds);
        }
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
    public async Task RobotsUsesCivicLensGroupInsteadOfWildcardAndCombinesConsecutiveAgents()
    {
        using var directory = new TemporaryDirectory();
        var robots = "User-agent: *\nDisallow: /watch\n\nUser-agent: CivicLens\nUser-agent: ExampleBot\nDisallow: /private\n";
        var handler = new QueueHandler(_ => Response(HttpStatusCode.OK, robots), _ => Response(HttpStatusCode.OK, "ok"));
        using var collector = new HttpCollector(handler);

        var result = await collector.FetchAsync(Request(directory.Path));

        Assert.Equal(CollectionOutcome.Captured, result.Outcome);
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task RobotsIgnoresGroupForDifferentProductTokenContainingCivicLens()
    {
        using var directory = new TemporaryDirectory();
        var robots = "User-agent: OtherCivicLens\nDisallow: /watch\n";
        var handler = new QueueHandler(_ => Response(HttpStatusCode.OK, robots), _ => Response(HttpStatusCode.OK, "ok"));
        using var collector = new HttpCollector(handler);

        var result = await collector.FetchAsync(Request(directory.Path));

        Assert.Equal(CollectionOutcome.Captured, result.Outcome);
    }

    [Fact]
    public async Task RobotsAllowsBlankLinesAndCarriageReturnLineEndingsWithinGroup()
    {
        using var directory = new TemporaryDirectory();
        var robots = "User-agent: CivicLens\r\nDisallow: /watch\r\n\r\nAllow: /watch/public\rCrawl-delay: 0\r";
        var handler = new QueueHandler(_ => Response(HttpStatusCode.OK, robots), _ => Response(HttpStatusCode.OK, "ok"));
        using var collector = new HttpCollector(handler);

        var result = await collector.FetchAsync(Request(directory.Path) with { Url = "https://example.test/watch/public" });

        Assert.Equal(CollectionOutcome.Captured, result.Outcome);
    }

    [Fact]
    public async Task RobotsUsesNormalizedOctetSpecificityForUnicodeRules()
    {
        using var directory = new TemporaryDirectory();
        var robots = "User-agent: CivicLens\nDisallow: /watch/caf%C3%A9\nAllow: /watch/café\n";
        var handler = new QueueHandler(_ => Response(HttpStatusCode.OK, robots), _ => Response(HttpStatusCode.OK, "ok"));
        using var collector = new HttpCollector(handler);

        var result = await collector.FetchAsync(Request(directory.Path) with { Url = "https://example.test/watch/caf%C3%A9" });

        Assert.Equal(CollectionOutcome.Captured, result.Outcome);
    }

    [Fact]
    public async Task RobotsUsesLongestRepeatedCrawlDelayInGroup()
    {
        using var directory = new TemporaryDirectory();
        var robots = "User-agent: CivicLens\nCrawl-delay: 2\nCrawl-delay: 1\n";
        var handler = new QueueHandler(_ => Response(HttpStatusCode.OK, robots));
        using var collector = new HttpCollector(handler);

        var result = await collector.FetchAsync(Request(directory.Path) with { TimeoutSeconds = 1, MinDelayMilliseconds = 0 });

        Assert.Equal(2000, result.RobotsCrawlDelayMilliseconds);
        Assert.Equal(2, result.RetryAfterSeconds);
    }

    [Fact]
    public async Task RobotsIgnoresRulesWithoutAbsolutePath()
    {
        using var directory = new TemporaryDirectory();
        var handler = new QueueHandler(_ => Response(HttpStatusCode.OK, "User-agent: CivicLens\nDisallow: watch\n"),
            _ => Response(HttpStatusCode.OK, "ok"));
        using var collector = new HttpCollector(handler);

        var result = await collector.FetchAsync(Request(directory.Path));

        Assert.Equal(CollectionOutcome.Captured, result.Outcome);
    }

    [Fact]
    public async Task RobotsAllowsMoreSpecificPathWhenDisallowAlsoMatches()
    {
        using var directory = new TemporaryDirectory();
        var robots = "User-agent: *\nDisallow: /watch\nAllow: /watch/public\n";
        var handler = new QueueHandler(_ => Response(HttpStatusCode.OK, robots), _ => Response(HttpStatusCode.OK, "ok"));
        using var collector = new HttpCollector(handler);

        var result = await collector.FetchAsync(Request(directory.Path) with { Url = "https://example.test/watch/public/story" });

        Assert.Equal(CollectionOutcome.Captured, result.Outcome);
    }

    [Fact]
    public async Task RobotsAllowsEquivalentRuleTie()
    {
        using var directory = new TemporaryDirectory();
        var robots = "User-agent: *\nDisallow: /watch\nAllow: /watch\n";
        var handler = new QueueHandler(_ => Response(HttpStatusCode.OK, robots), _ => Response(HttpStatusCode.OK, "ok"));
        using var collector = new HttpCollector(handler);

        var result = await collector.FetchAsync(Request(directory.Path));

        Assert.Equal(CollectionOutcome.Captured, result.Outcome);
    }

    [Fact]
    public async Task RobotsRedirectCanLeaveContentPathButMustStayOnOrigin()
    {
        using var directory = new TemporaryDirectory();
        var handler = new QueueHandler(
            _ => new HttpResponseMessage(HttpStatusCode.Found) { Headers = { Location = new Uri("/policy/robots.txt", UriKind.Relative) } },
            _ => Response(HttpStatusCode.OK, "User-agent: *\n"),
            _ => Response(HttpStatusCode.OK, "ok"));
        using var collector = new HttpCollector(handler);

        var result = await collector.FetchAsync(Request(directory.Path));

        Assert.Equal(CollectionOutcome.Captured, result.Outcome);
        Assert.Equal(3, result.RequestCount);
        Assert.Equal(2, result.RobotsRequestCount);
    }

    [Fact]
    public async Task MultiHopRobotsFailureKeepsConditionalValidatorsUnsent()
    {
        using var directory = new TemporaryDirectory();
        var handler = new QueueHandler(
            _ => new HttpResponseMessage(HttpStatusCode.Found) { Headers = { Location = new Uri("/robots-one", UriKind.Relative) } },
            _ => new HttpResponseMessage(HttpStatusCode.Found) { Headers = { Location = new Uri("/robots-two", UriKind.Relative) } },
            _ => Response(HttpStatusCode.ServiceUnavailable));
        using var collector = new HttpCollector(handler);

        var result = await collector.FetchAsync(Request(directory.Path) with { ETag = "\"prior\"" });

        Assert.Equal(CollectionOutcome.Deferred, result.Outcome);
        Assert.Equal(3, result.RequestCount);
        Assert.Equal(3, result.RobotsRequestCount);
        Assert.Null(result.SentValidators);
        Assert.Null(result.Response);
    }

    [Fact]
    public async Task LegacyRobotsRedirectIsRejectedWithoutChangingContentRequestInference()
    {
        using var directory = new TemporaryDirectory();
        var handler = new QueueHandler(_ => Redirect("https://example.test/robots-copy.txt"));
        using var collector = new HttpCollector(handler);

        var result = await collector.FetchAsync(Request(directory.Path) with
        {
            Version = CollectionProtocol.PreviousVersion,
            ETag = "\"prior\""
        });

        Assert.Equal(CollectionOutcome.Failed, result.Outcome);
        Assert.Equal(CollectionFailureCode.RobotsUnavailable, result.FailureCode);
        Assert.Equal(1, result.RequestCount);
        Assert.Null(result.SentValidators);
    }

    [Fact]
    public async Task RobotsRedirectLoopStopsAtSharedRequestBudget()
    {
        using var directory = new TemporaryDirectory();
        var handler = new QueueHandler(_ => Redirect("https://example.test/robots.txt"),
            _ => Redirect("https://example.test/robots.txt"), _ => Redirect("https://example.test/robots.txt"));
        using var collector = new HttpCollector(handler);
        var result = await collector.FetchAsync(Request(directory.Path) with { MaxRequests = 3, ETag = "\"v1\"" });
        Assert.Equal(CollectionFailureCode.RequestBudget, result.FailureCode);
        Assert.Equal(3, result.RobotsRequestCount);
        Assert.Equal(3, result.RequestCount);
        Assert.Null(result.SentValidators);
        Assert.Null(result.Capture);
    }

    [Fact]
    public async Task RobotsCrossOriginRedirectFailsClosed()
    {
        using var directory = new TemporaryDirectory();
        var handler = new QueueHandler(_ => Redirect("https://outside.test/robots.txt"));
        using var collector = new HttpCollector(handler);

        var result = await collector.FetchAsync(Request(directory.Path));

        Assert.Equal(CollectionOutcome.Failed, result.Outcome);
        Assert.Equal(CollectionFailureCode.RobotsUnavailable, result.FailureCode);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task RobotsBodySupportsBoundedGzipDecoding()
    {
        using var directory = new TemporaryDirectory();
        using var compressed = new MemoryStream();
        using (var gzip = new GZipStream(compressed, CompressionMode.Compress, leaveOpen: true))
            gzip.Write(Encoding.UTF8.GetBytes("User-agent: *\nDisallow: /private\n"));
        var handler = new QueueHandler(_ =>
        {
            var response = Response(HttpStatusCode.OK, compressed.ToArray());
            response.Content.Headers.ContentEncoding.Add("gzip");
            return response;
        }, _ => Response(HttpStatusCode.OK, "ok"));
        using var collector = new HttpCollector(handler);

        var result = await collector.FetchAsync(Request(directory.Path));

        Assert.Equal(CollectionOutcome.Captured, result.Outcome);
    }

    [Fact]
    public async Task RobotsNestedEncodingRejectsCorruptInnerTrailer()
    {
        using var directory = new TemporaryDirectory();
        var inner = Gzip(Encoding.UTF8.GetBytes("User-agent: *\n"));
        inner[^8] ^= 0xff;
        var outer = Gzip(inner);
        var handler = new QueueHandler(_ =>
        {
            var response = Response(HttpStatusCode.OK, outer);
            response.Content.Headers.ContentEncoding.Add("gzip");
            response.Content.Headers.ContentEncoding.Add("gzip");
            return response;
        });
        using var collector = new HttpCollector(handler);

        var result = await collector.FetchAsync(Request(directory.Path));

        Assert.Equal(CollectionOutcome.Failed, result.Outcome);
        Assert.Equal(CollectionFailureCode.RobotsUnavailable, result.FailureCode);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task RobotsNestedEncodingBoundsIntermediateExpansionAndDrainsStages()
    {
        using var directory = new TemporaryDirectory();
        var inner = Gzip(Encoding.UTF8.GetBytes("User-agent: *\n"));
        var expandedIntermediate = new byte[10_000_001];
        var intermediary = inner.Concat(expandedIntermediate).ToArray();
        var outer = Gzip(intermediary);
        var handler = new QueueHandler(_ =>
        {
            var response = Response(HttpStatusCode.OK, outer);
            response.Content.Headers.ContentEncoding.Add("gzip");
            response.Content.Headers.ContentEncoding.Add("gzip");
            return response;
        });
        using var collector = new HttpCollector(handler);

        var result = await collector.FetchAsync(Request(directory.Path) with { MaxBytes = 100_000 });

        Assert.Equal(CollectionOutcome.Failed, result.Outcome);
        Assert.Equal(CollectionFailureCode.RobotsUnavailable, result.FailureCode);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task RobotsRateLimitDefersWithoutInventingContentResponse()
    {
        using var directory = new TemporaryDirectory();
        var handler = new QueueHandler(_ =>
        {
            var response = new HttpResponseMessage((HttpStatusCode)429);
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(9));
            return response;
        });
        using var collector = new HttpCollector(handler);

        var result = await collector.FetchAsync(Request(directory.Path) with { ETag = "\"prior\"" });

        Assert.Equal(CollectionOutcome.Deferred, result.Outcome);
        Assert.Equal(CollectionFailureCode.RateLimited, result.FailureCode);
        Assert.Equal(9, result.RetryAfterSeconds);
        Assert.Null(result.Response);
        Assert.Null(result.SentValidators);
        Assert.Equal(1, result.RobotsRequestCount);
    }

    [Fact]
    public async Task RobotsServerFailureDefersForRetry()
    {
        using var directory = new TemporaryDirectory();
        var handler = new QueueHandler(_ => Response(HttpStatusCode.ServiceUnavailable));
        using var collector = new HttpCollector(handler);

        var result = await collector.FetchAsync(Request(directory.Path));

        Assert.Equal(CollectionOutcome.Deferred, result.Outcome);
        Assert.Equal(CollectionFailureCode.RobotsUnavailable, result.FailureCode);
        Assert.Null(result.Response);
        Assert.Equal(1, result.RobotsRequestCount);
    }

    [Theory]
    [InlineData("0.06", 0)]
    [InlineData("0.02", 60)]
    public async Task ContentRequestsHonorTheGreaterOfConfiguredAndDeclaredPacing(string crawlDelay, int configuredDelay)
    {
        using var directory = new TemporaryDirectory();
        var requestsAt = new List<long>();
        var handler = new QueueHandler(_ =>
        {
            requestsAt.Add(System.Diagnostics.Stopwatch.GetTimestamp());
            return Response(HttpStatusCode.OK, $"User-agent: *\nCrawl-delay: {crawlDelay}\n");
        }, _ =>
        {
            requestsAt.Add(System.Diagnostics.Stopwatch.GetTimestamp());
            return Redirect("https://example.test/watch/next");
        }, _ =>
        {
            requestsAt.Add(System.Diagnostics.Stopwatch.GetTimestamp());
            return Response(HttpStatusCode.OK, "content");
        });
        using var collector = new HttpCollector(handler);
        var result = await collector.FetchAsync(Request(directory.Path) with { MinDelayMilliseconds = configuredDelay });
        Assert.Equal(CollectionOutcome.Captured, result.Outcome);
        Assert.Equal(3, requestsAt.Count);
        for (var index = 1; index < requestsAt.Count; index++)
            Assert.True(System.Diagnostics.Stopwatch.GetElapsedTime(requestsAt[index - 1], requestsAt[index]).TotalMilliseconds >= 50);
    }

    [Fact]
    public async Task RobotsCrawlDelayThatExceedsTimeoutDefersWithRequiredWait()
    {
        using var directory = new TemporaryDirectory();
        var handler = new QueueHandler(_ => Response(HttpStatusCode.OK, "User-agent: *\nCrawl-delay: 1.25\n"));
        using var collector = new HttpCollector(handler);

        var result = await collector.FetchAsync(Request(directory.Path) with { TimeoutSeconds = 1, MinDelayMilliseconds = 0 });

        Assert.Equal(CollectionOutcome.Deferred, result.Outcome);
        Assert.Equal(CollectionFailureCode.CrawlDelay, result.FailureCode);
        Assert.Equal(1250, result.RobotsCrawlDelayMilliseconds);
        Assert.Equal(2, result.RetryAfterSeconds);
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
    private static byte[] Gzip(byte[] bytes)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionMode.Compress, leaveOpen: true)) gzip.Write(bytes);
        return output.ToArray();
    }

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
