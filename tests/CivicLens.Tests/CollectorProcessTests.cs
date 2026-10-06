using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using CivicLens.Application;
using CivicLens.Collection.Contracts;
using CivicLens.Collector;
using CivicLens.Infrastructure;

namespace CivicLens.Tests;

public sealed class CollectorProcessTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RealProcessVerifiesCapturesAndRejectsIncompleteHttpBodies(bool truncate)
    {
        var directory = Path.Combine(Path.GetTempPath(), "civic-process-" + Guid.NewGuid().ToString("N"));
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var origin = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}";
        var payload = Encoding.UTF8.GetBytes("Exact source bytes.\r\n");
        var server = ServeAsync(listener, payload, truncate, truncate ? 2 : 4, deadline.Token);
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
                Assert.Equal("incompleteResponse", result.FailureCode);
                Assert.Empty(Directory.GetFiles(directory, "*.gz"));
            }
            else
            {
                Assert.Equal(CollectionOutcome.Captured, result.Outcome);
                Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(payload)), result.Sha256);
                var repeated = await useCase.ExecuteAsync(configuration, "source", directory, deadline.Token);
                Assert.Equal(CollectionOutcome.Captured, repeated.Outcome);
                Assert.NotEqual(result.JobId, repeated.JobId);
                Assert.Equal(result.ArtifactPath, repeated.ArtifactPath);
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
    public async Task CancellingChildReleasesDirectoryLeaseAndRemovesManifest()
    {
        var directory = Path.Combine(Path.GetTempPath(), "civic-cancel-" + Guid.NewGuid().ToString("N"));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var origin = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}";
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var cancelled = new CancellationTokenSource();
        var request = CollectionProtocolTests.Request() with
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

    private static async Task ServeAsync(TcpListener listener, byte[] payload, bool truncate, int count, CancellationToken token)
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
            var header = Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Length: {length}\r\nConnection: close\r\n\r\n");
            await stream.WriteAsync(header, token);
            await stream.WriteAsync(body, token);
        }
    }
}
