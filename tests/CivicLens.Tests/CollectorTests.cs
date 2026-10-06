using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using CivicLens.Collection.Contracts;
using CivicLens.Collector;

namespace CivicLens.Tests;

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
        Assert.Equal(Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant(), result.Sha256);
        Assert.Equal(2, result.RequestCount);
        await using var file = File.OpenRead(Path.Combine(directory, result.ArtifactPath!));
        await using var gzip = new GZipStream(file, CompressionMode.Decompress);
        using var output = new MemoryStream(); await gzip.CopyToAsync(output);
        Assert.Equal(payload, output.ToArray());
    }

    [Fact]
    public async Task BomPrefixedRobotsDenyPreventsTargetRequest()
    {
        var handler = new QueueHandler(_ => Response(HttpStatusCode.OK, "\uFEFFUser-agent: *\nDisallow: /watch\n"));
        using var collector = new HttpCollector(handler);
        using var directoryOwner = new TemporaryDirectory();
        var result = await collector.FetchAsync(Request(directoryOwner.Path));
        Assert.Equal("robotsDenied", result.FailureCode);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task RobotsDenyPreventsTargetRequest()
    {
        using var directoryOwner = new TemporaryDirectory();
        var handler = new QueueHandler(_ => Response(HttpStatusCode.OK, "User-agent: *\nDisallow: /watch*\n"));
        using var collector = new HttpCollector(handler);
        var result = await collector.FetchAsync(Request(directoryOwner.Path));
        Assert.Equal("robotsDenied", result.FailureCode);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task RobotsRulesMatchPercentEncodedUnreservedPathCharacters()
    {
        using var directoryOwner = new TemporaryDirectory();
        var handler = new QueueHandler(_ => Response(HttpStatusCode.OK, "User-agent: *\nDisallow: /watch/secret\n"));
        using var collector = new HttpCollector(handler);
        var result = await collector.FetchAsync(Request(directoryOwner.Path) with { Url = "https://example.test/watch/%73ecret" });
        Assert.Equal("robotsDenied", result.FailureCode);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task RobotsUnicodeRuleMatchesPercentEncodedUrl()
    {
        using var directoryOwner = new TemporaryDirectory();
        var handler = new QueueHandler(_ => Response(HttpStatusCode.OK, "User-agent: *\nDisallow: /watch/café\n"));
        using var collector = new HttpCollector(handler);
        var result = await collector.FetchAsync(Request(directoryOwner.Path) with { Url = "https://example.test/watch/caf%C3%A9" });
        Assert.Equal("robotsDenied", result.FailureCode);
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
        Assert.Equal("robotsUnavailable", result.FailureCode);
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
        Assert.Equal("robotsUnavailable", result.FailureCode);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task RejectsOutOfScopeRedirectBeforeSecondRequest()
    {
        using var directoryOwner = new TemporaryDirectory();
        var handler = new QueueHandler(_ => Response(HttpStatusCode.NotFound), _ => Redirect("https://outside.test/watch"));
        using var collector = new HttpCollector(handler);
        var result = await collector.FetchAsync(Request(directoryOwner.Path));
        Assert.Equal("outOfScope", result.FailureCode);
        Assert.Equal(2, handler.Calls);
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
        Assert.Equal("oversized", result.FailureCode);
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
        Assert.Equal("incompleteResponse", result.FailureCode);
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
        Assert.Equal("cancelled", result.FailureCode);
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
