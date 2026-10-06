using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using CivicLens.Application.Collection;
using CivicLens.Collection.Contracts;
using CivicLens.Collector.Http;
using CivicLens.Infrastructure.Collection;
using static CivicLens.Tests.Fixtures.CollectionFixtures;

namespace CivicLens.Tests.Infrastructure.Collection;

public sealed class CollectorProcessTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task RealProcessVerifiesCapturesAndRejectsIncompleteHttpBodies(bool truncate, bool encoded)
    {
        var directory = Path.Combine(Path.GetTempPath(), "civic-process-" + Guid.NewGuid().ToString("N"));
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var origin = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}";
        var payload = Encoding.UTF8.GetBytes("Exact source bytes.\r\n");
        if (encoded)
        {
            using var memory = new MemoryStream();
            using (var gzip = new GZipStream(memory, CompressionMode.Compress, leaveOpen: true)) gzip.Write(payload);
            payload = memory.ToArray();
        }
        var server = ServeAsync(listener, payload, truncate, encoded, truncate ? 2 : 4, deadline.Token);
        try
        {
            var configuration = new CollectionConfiguration
            {
                People = [new PersonConfiguration { Id = "person", Name = "Fixture Official" }],
                Sources = [new WatchedSourceConfiguration
                {
                    Id = "source", PersonIds = ["person"], Url = origin + "/watch", AllowedOrigin = origin,
                    AllowedPathPrefix = "/watch", MinDelayMilliseconds = 0, TimeoutSeconds = 5
                }]
            };
            var useCase = new CollectWatchedPage(new CollectorProcess(typeof(HttpCollector).Assembly.Location));
            var result = await useCase.ExecuteAsync(configuration, "source", directory, deadline.Token);
            if (truncate)
            {
                Assert.Equal(CollectionOutcome.Failed, result.Outcome);
                Assert.Equal(CollectionFailureCode.IncompleteResponse, result.FailureCode);
                Assert.Empty(Directory.GetFiles(directory, "*.gz"));
            }
            else
            {
                Assert.Equal(CollectionOutcome.Captured, result.Outcome);
                Assert.Equal("text/plain; charset=utf-8", result.Response!.ContentType);
                Assert.Equal(new DateTimeOffset(2026, 10, 6, 12, 0, 0, TimeSpan.Zero), result.Response.LastModified);
                Assert.Equal("\"fixture-v1\"", result.Response.ETag);
                Assert.Equal(encoded ? new[] { "gzip" } : [], result.Response.ContentEncodings);
                Assert.Equal(payload.Length, result.Capture!.ByteLength);
                Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(payload)), result.Capture!.Sha256);
                var repeated = await useCase.ExecuteAsync(configuration, "source", directory, deadline.Token);
                Assert.Equal(CollectionOutcome.Captured, repeated.Outcome);
                Assert.NotEqual(result.JobId, repeated.JobId);
                Assert.Equal(result.Capture!.RelativePath, repeated.Capture!.RelativePath);
                Assert.Single(Directory.GetFiles(directory, "*.gz"));
            }
            Assert.Empty(Directory.GetFiles(directory, ".request-*"));
            Assert.Empty(Directory.GetFiles(directory, ".capture-*"));
            await server;
        }
        finally
        {
            deadline.Cancel();
            listener.Stop();
            try { await server; } catch (Exception e) when (e is OperationCanceledException or SocketException) { }
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task RealProcessReceiptMatchesConditionalHeadersReceivedByServer()
    {
        var directory = Path.Combine(Path.GetTempPath(), "civic-validators-" + Guid.NewGuid().ToString("N"));
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var origin = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}";
        var modified = new DateTimeOffset(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);
        var request = Request() with
        {
            Url = origin + "/watch",
            AllowedOrigin = origin,
            AllowedPathPrefix = "/watch",
            ArtifactDirectory = directory,
            MinDelayMilliseconds = 0,
            ETag = "  W/\"v1\"  ",
            LastModified = modified.AddTicks(1234567).ToOffset(TimeSpan.FromHours(2))
        };
        var server = ServeConditionalAsync();
        try
        {
            var result = await new CollectorProcess(typeof(HttpCollector).Assembly.Location).RunAsync(request, deadline.Token);
            var headers = await server;

            Assert.Equal(CollectionOutcome.NotModified, result.Outcome);
            Assert.Equal(new HttpRequestValidators { ETag = "W/\"v1\"", LastModified = modified }, result.SentValidators);
            Assert.Contains("If-None-Match: " + result.SentValidators!.ETag, headers);
            Assert.Contains("If-Modified-Since: Tue, 06 Oct 2026 12:00:00 GMT", headers);
        }
        finally
        {
            deadline.Cancel();
            listener.Stop();
            try { await server; } catch (Exception e) when (e is OperationCanceledException or SocketException) { }
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }

        async Task<List<string>> ServeConditionalAsync()
        {
            var headers = new List<string>();
            for (var i = 0; i < 2; i++)
            {
                using var socket = await listener.AcceptTcpClientAsync(deadline.Token);
                await using var stream = socket.GetStream();
                using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
                headers.Clear();
                while (await reader.ReadLineAsync(deadline.Token) is { Length: > 0 } line)
                    headers.Add(line);
                if (i == 0)
                    Assert.DoesNotContain(headers, header => header.StartsWith("If-", StringComparison.OrdinalIgnoreCase));
                var status = i == 0 ? "404 Not Found" : "304 Not Modified";
                await stream.WriteAsync(Encoding.ASCII.GetBytes(
                    $"HTTP/1.1 {status}\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"), deadline.Token);
            }
            return headers;
        }
    }

    [Fact]
    public async Task CancellingChildReleasesDirectoryLeaseAndRemovesManifest()
    {
        var directory = Path.Combine(Path.GetTempPath(), "civic-cancel-" + Guid.NewGuid().ToString("N"));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var origin = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}";
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var cancelled = new CancellationTokenSource();
        var request = Request() with
        {
            Url = origin + "/watch",
            AllowedOrigin = origin,
            AllowedPathPrefix = "/watch",
            ArtifactDirectory = directory
        };
        var runner = new CollectorProcess(typeof(HttpCollector).Assembly.Location);
        var run = runner.RunAsync(request, cancelled.Token);
        try
        {
            using var socket = await listener.AcceptTcpClientAsync(deadline.Token);
            cancelled.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run.WaitAsync(deadline.Token));
            Assert.Empty(Directory.GetFiles(directory, ".request-*"));
            using var lease = new FileStream(Path.Combine(directory, ".collector.lock"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }
        finally
        {
            cancelled.Cancel();
            try { await run; } catch (OperationCanceledException) { }
            listener.Stop();
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    private static async Task ServeAsync(TcpListener listener, byte[] payload, bool truncate, bool encoded, int count, CancellationToken token)
    {
        for (var i = 0; i < count; i++)
        {
            using var socket = await listener.AcceptTcpClientAsync(token);
            await using var stream = socket.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
            var requestLine = await reader.ReadLineAsync(token);
            while (!string.IsNullOrEmpty(await reader.ReadLineAsync(token))) { }
            var robots = requestLine!.Contains(" /robots.txt ", StringComparison.Ordinal);
            var body = robots ? Encoding.UTF8.GetBytes("User-agent: *\nDisallow:\n") : payload;
            var length = body.Length + (!robots && truncate ? 100 : 0);
            var metadata = robots ? "" : "Content-Type: text/plain; charset=utf-8\r\nLast-Modified: Tue, 06 Oct 2026 12:00:00 GMT\r\nETag: \"fixture-v1\"\r\n" + (encoded ? "Content-Encoding: gzip\r\n" : "");
            var header = Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Length: {length}\r\n{metadata}Connection: close\r\n\r\n");
            await stream.WriteAsync(header, token);
            await stream.WriteAsync(body, token);
        }
    }
}
