using System.Net;
using System.Text;
using CivicLens.Collection.Contracts;
using CivicLens.Collector.Http;

namespace CivicLens.Tests.Collector.Http;

public sealed class RobotsCacheTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "robots-cache-" + Guid.NewGuid().ToString("N"));
    private readonly ManualClock clock = new();

    public RobotsCacheTests() => Directory.CreateDirectory(directory);

    public void Dispose() => Directory.Delete(directory, true);

    [Fact]
    public async Task FreshCacheSendsNoRobotsRequestAndStillReportsTheCrawlDelay()
    {
        var site = new Site(robots: Ok("User-agent: *\nCrawl-delay: 0\n"));

        await FetchAsync(site);
        var cached = await FetchAsync(site);

        Assert.Equal(["/robots.txt", "/watch", "/watch"], site.Paths);
        Assert.Equal(CollectionOutcome.Captured, cached.Outcome);
        Assert.Equal(0, cached.RobotsRequestCount);
        Assert.True(cached.RobotsCacheHit);
        Assert.Equal(1, cached.RequestCount);
        Assert.Equal(0, cached.RobotsCrawlDelayMilliseconds);
    }

    [Fact]
    public async Task MissingRobotsFileIsCachedSoItIsNotRequestedEveryJob()
    {
        var site = new Site(robots: () => new HttpResponseMessage(HttpStatusCode.NotFound));

        await FetchAsync(site);
        var cached = await FetchAsync(site);

        Assert.Equal(["/robots.txt", "/watch", "/watch"], site.Paths);
        Assert.Equal(CollectionOutcome.Captured, cached.Outcome);
    }

    [Theory]
    [InlineData(HttpStatusCode.Gone)]
    [InlineData(HttpStatusCode.BadRequest)]
    public async Task UnavailableClientErrorPermitsCollectionAndIsCachedLikeAMissingFile(HttpStatusCode status)
    {
        var site = new Site(robots: () => new HttpResponseMessage(status));

        var first = await FetchAsync(site);
        var cached = await FetchAsync(site);

        Assert.Equal(["/robots.txt", "/watch", "/watch"], site.Paths);
        Assert.Equal(CollectionOutcome.Captured, first.Outcome);
        Assert.Equal(CollectionOutcome.Captured, cached.Outcome);
        Assert.True(cached.RobotsCacheHit);
    }

    [Fact]
    public async Task CachedUnavailableClientErrorIsNeverUsedAsAStaleCopy()
    {
        var site = new Site(robots: () => new HttpResponseMessage(HttpStatusCode.Gone));
        await FetchAsync(site);
        site.Robots = () => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);

        clock.Advance(TimeSpan.FromDays(2));
        var result = await FetchAsync(site);

        Assert.Equal(CollectionFailureCode.RobotsUnavailable, result.FailureCode);
        Assert.False(result.RobotsCacheHit);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.Unauthorized)]
    public async Task RobotsAccessDeniedBlocksCollectionBecauseAFirewallLikelyBlocksTheCollector(HttpStatusCode status)
    {
        var site = new Site(robots: () => new HttpResponseMessage(status));

        var result = await FetchAsync(site);
        await FetchAsync(site);

        Assert.Equal(CollectionOutcome.Failed, result.Outcome);
        Assert.Equal(CollectionFailureCode.RobotsUnavailable, result.FailureCode);
        Assert.Equal(["/robots.txt", "/robots.txt"], site.Paths);
        Assert.Empty(Directory.GetFiles(directory, "robots-*.json"));
    }

    [Fact]
    public async Task OnlyAFreshFetchIsNotACacheHit()
    {
        var site = new Site(robots: Ok("User-agent: *\nDisallow:\n"));

        var first = await FetchAsync(site);

        Assert.False(first.RobotsCacheHit);
        Assert.Equal(1, first.RobotsRequestCount);
    }

    [Fact]
    public async Task ServerErrorAfterACachedNotFoundBehavesAsWithoutACache()
    {
        var site = new Site(robots: () => new HttpResponseMessage(HttpStatusCode.NotFound));
        await FetchAsync(site);
        site.Robots = () => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);

        clock.Advance(TimeSpan.FromDays(2));
        var result = await FetchAsync(site);

        Assert.Equal(CollectionOutcome.Deferred, result.Outcome);
        Assert.Equal(CollectionFailureCode.RobotsUnavailable, result.FailureCode);
        Assert.False(result.RobotsCacheHit);
    }

    [Fact]
    public async Task ExpiredCacheIsRefetchedBecauseRulesCanChange()
    {
        var site = new Site(robots: Ok("User-agent: *\nDisallow:\n"));
        await FetchAsync(site);

        clock.Advance(TimeSpan.FromHours(24) + TimeSpan.FromMinutes(1));
        await FetchAsync(site);

        Assert.Equal(["/robots.txt", "/watch", "/robots.txt", "/watch"], site.Paths);
    }

    [Fact]
    public async Task DisallowFromCacheStillDeniesWithoutAnyRequest()
    {
        var site = new Site(robots: Ok("User-agent: *\nDisallow: /watch\n"));
        await FetchAsync(site);

        var denied = await FetchAsync(site);

        Assert.Equal(CollectionFailureCode.RobotsDenied, denied.FailureCode);
        Assert.Equal(["/robots.txt"], site.Paths);
    }

    [Fact]
    public async Task StaleCopyIsUsedWhenRefetchGetsServerError()
    {
        var site = new Site(robots: Ok("User-agent: *\nDisallow:\n"));
        await FetchAsync(site);
        site.Robots = () => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);

        clock.Advance(TimeSpan.FromDays(2));
        var result = await FetchAsync(site);

        Assert.Equal(CollectionOutcome.Captured, result.Outcome);
        Assert.Equal(1, result.RobotsRequestCount);
        Assert.True(result.RobotsCacheHit);
    }

    [Fact]
    public async Task RobotsRateLimitStillDefersEvenWithAUsableStaleCopySoTheGroupBacksOff()
    {
        var site = new Site(robots: Ok("User-agent: *\nDisallow:\n"));
        await FetchAsync(site);
        site.Robots = () => new HttpResponseMessage(HttpStatusCode.TooManyRequests);

        clock.Advance(TimeSpan.FromDays(2));
        var result = await FetchAsync(site);

        Assert.Equal(CollectionOutcome.Deferred, result.Outcome);
        Assert.Equal(CollectionFailureCode.RateLimited, result.FailureCode);
        Assert.Equal(["/robots.txt", "/watch", "/robots.txt"], site.Paths);
    }

    [Fact]
    public async Task StaleCopyStillObeysItsDisallow()
    {
        var site = new Site(robots: Ok("User-agent: *\nDisallow: /watch\n"));
        await FetchAsync(site);
        site.Robots = () => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);

        clock.Advance(TimeSpan.FromDays(2));
        var result = await FetchAsync(site);

        Assert.Equal(CollectionFailureCode.RobotsDenied, result.FailureCode);
    }

    [Fact]
    public async Task CopyOlderThanSevenDaysIsNotUsedWhenRefetchFails()
    {
        var site = new Site(robots: Ok("User-agent: *\nDisallow:\n"));
        await FetchAsync(site);
        site.Robots = () => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);

        clock.Advance(TimeSpan.FromDays(7) + TimeSpan.FromMinutes(1));
        var result = await FetchAsync(site);

        Assert.Equal(CollectionOutcome.Deferred, result.Outcome);
        Assert.Equal(CollectionFailureCode.RobotsUnavailable, result.FailureCode);
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.ProxyAuthenticationRequired)]
    public async Task TemporaryRobotsResponsesDeferAndAreNeverCached(HttpStatusCode status)
    {
        var site = new Site(robots: () => new HttpResponseMessage(status));
        var failed = await FetchAsync(site);
        site.Robots = Ok("User-agent: *\nDisallow:\n");

        var recovered = await FetchAsync(site);

        Assert.Equal(CollectionOutcome.Deferred, failed.Outcome);
        Assert.Equal(CollectionOutcome.Captured, recovered.Outcome);
        Assert.Equal(["/robots.txt", "/robots.txt", "/watch"], site.Paths);
    }

    [Fact]
    public async Task CorruptCacheFileIsRefetched()
    {
        var site = new Site(robots: Ok("User-agent: *\nDisallow:\n"));
        await FetchAsync(site);
        foreach (var file in Directory.GetFiles(directory, "robots-*.json")) File.WriteAllText(file, "{ not json");

        var result = await FetchAsync(site);

        Assert.Equal(CollectionOutcome.Captured, result.Outcome);
        Assert.Equal(1, result.RobotsRequestCount);
    }

    [Fact]
    public async Task CacheIsKeyedByOriginSoAnotherOriginFetchesItsOwnRobots()
    {
        var site = new Site(robots: Ok("User-agent: *\nDisallow:\n"));
        await FetchAsync(site);

        var other = await FetchAsync(site, "https://other.example.test");

        Assert.Equal(1, other.RobotsRequestCount);
        Assert.Equal(2, site.Paths.Count(path => path == "/robots.txt"));
    }

    [Fact]
    public async Task ATimeoutWhileReadingTheCacheIsATimeoutFailureBeforeAnyRequest()
    {
        var site = new Site(robots: Ok("User-agent: *\nDisallow:\n"));
        await FetchAsync(site);

        var result = await FetchAsync(site, timeProvider: new ExpiredDeadlineClock());

        Assert.Equal(CollectionOutcome.Failed, result.Outcome);
        Assert.Equal(CollectionFailureCode.Timeout, result.FailureCode);
        Assert.Equal(0, result.RequestCount);
        Assert.Equal(["/robots.txt", "/watch"], site.Paths);
    }

    [Fact]
    public async Task AFreshCacheHitWithACrawlDelayDoesNotWaitBeforeTheFirstContentRequest()
    {
        var site = new Site(robots: Ok("User-agent: *\nCrawl-delay: 3600\n"));
        await FetchAsync(site);

        var cached = await FetchAsync(site);

        Assert.Equal(CollectionOutcome.Captured, cached.Outcome);
        Assert.Equal(3_600_000, cached.RobotsCrawlDelayMilliseconds);
        Assert.True(cached.RobotsCacheHit);
    }

    [Fact]
    public async Task AFailedCacheWriteDoesNotFailTheCollection()
    {
        var key = Encoding.UTF8.GetBytes("https://example.test:443");
        Directory.CreateDirectory(Path.Combine(directory,
            "robots-" + Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(key)) + ".json"));
        var site = new Site(robots: Ok("User-agent: *\nDisallow:\n"));

        var first = await FetchAsync(site);
        var second = await FetchAsync(site);

        Assert.Equal(CollectionOutcome.Captured, first.Outcome);
        Assert.Equal(CollectionOutcome.Captured, second.Outcome);
        Assert.Equal(1, second.RobotsRequestCount);
        Assert.Empty(Directory.GetFiles(directory, ".robots-*.tmp"));
    }

    private async Task<CollectionResult> FetchAsync(Site site, string origin = "https://example.test",
        TimeProvider? timeProvider = null)
    {
        using var collector = new HttpCollector(site, clock: timeProvider ?? clock);
        return await collector.FetchAsync(new CollectionRequest
        {
            JobId = "job",
            SourceId = "source",
            Url = origin + "/watch",
            AllowedOrigin = origin,
            AllowedPathPrefix = "/watch",
            ArtifactDirectory = directory,
            MaxRequests = 5,
            MaxBytes = 1000,
            MinDelayMilliseconds = 0
        });
    }

    private static Func<HttpResponseMessage> Ok(string robots) =>
        () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Encoding.UTF8.GetBytes(robots)) };

    private sealed class Site(Func<HttpResponseMessage> robots) : HttpMessageHandler
    {
        public Func<HttpResponseMessage> Robots { get; set; } = robots;
        public List<string> Paths { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Paths.Add(request.RequestUri!.AbsolutePath);
            return Task.FromResult(request.RequestUri.AbsolutePath == "/robots.txt"
                ? Robots()
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent("ok"u8.ToArray()) });
        }
    }

    private sealed class ExpiredDeadlineClock : TimeProvider
    {
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            callback(state);
            return base.CreateTimer(callback, state, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }
    }

    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset now = new(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);
        public void Advance(TimeSpan span) => now += span;
        public override DateTimeOffset GetUtcNow() => now;
    }
}
