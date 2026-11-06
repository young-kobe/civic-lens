using System.Net;
using CivicLens.Host.Publication;
using CivicLens.PublicApi;
using CivicLens.PublicApi.Pages;
using CivicLens.PublicApi.ActiveRelease;
using CivicLens.PublicWeb.Publication;
using CivicLens.Publication.Contracts;
using CivicLens.Tests.Infrastructure.Collection;
using CivicLens.Tests.PublicApi.Votes;
using CivicLens.Tests.Fixtures;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace CivicLens.Tests.PublicApi.Pages;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class ServerPageTests(PostgresCollection postgres)
{
    private const string UnknownPage = ServerPageEndpoints.Prefix + "/no-such-page";
    private static readonly PublicBaseUrl BaseUrl = PublicBaseUrl.Parse("https://civic-lens.test", "test")!;
    private static readonly SeededVote Vote = new('e', "2026-03-03");
    private static readonly DateTimeOffset PublishedAt = new(2026, 6, 9, 12, 0, 0, TimeSpan.Zero);

    private Task<ApiHost> StartAsync(bool withBaseUrl = true) =>
        ApiHost.StartAsync(postgres, new FakeReleaseSite(), options => options with { PublicBaseUrl = withBaseUrl ? BaseUrl : null });

    [Fact]
    public async Task AnUnknownServerPageAnswers404WithTheETagOfTheActiveReleaseAndBuild()
    {
        await using var host = await StartAsync();
        await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 7, PublishedAt);
        await ViewsSeed.AddVotesAsync(host.OperationalDatabase, Vote);

        var response = await host.Client.GetAsync(UnknownPage);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal($"\"r7-{BuildId.FromAssembly(typeof(PublicApiApp).Assembly)}\"", response.Headers.ETag!.Tag);
        Assert.Equal("text/html; charset=utf-8", response.Content.Headers.ContentType!.ToString());
    }

    [Fact]
    public async Task TheETagChangesWithTheReleaseSoARollbackOrPublishReplacesTheCachedPage()
    {
        await using var host = await StartAsync();
        await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 1, PublishedAt);
        await ViewsSeed.AddVotesAsync(host.OperationalDatabase, Vote);
        var first = (await host.Client.GetAsync(UnknownPage)).Headers.ETag;
        await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 2, PublishedAt.AddDays(7));

        var second = (await host.Client.GetAsync(UnknownPage)).Headers.ETag;

        Assert.NotEqual(first, second);
    }

    [Fact]
    public async Task EveryServerPageResponseCarriesTheSharedCacheControlValue()
    {
        await using var host = await StartAsync();
        await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 1, PublishedAt);
        await ViewsSeed.AddVotesAsync(host.OperationalDatabase, Vote);

        var fresh = await host.Client.GetAsync(UnknownPage);
        var revalidated = await Revalidate(host, fresh);

        Assert.Equal("public, s-maxage=60, stale-while-revalidate=600, stale-if-error=86400", string.Join(", ", fresh.Headers.GetValues("Cache-Control")));
        Assert.Equal(string.Join(", ", fresh.Headers.GetValues("Cache-Control")), string.Join(", ", revalidated.Headers.GetValues("Cache-Control")));
    }

    [Fact]
    public async Task ANotFoundPageIgnoresMatchingWeakAndWildcardValidatorsAndKeepsIts404Body()
    {
        await using var host = await StartAsync();
        await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 3, PublishedAt);
        await ViewsSeed.AddVotesAsync(host.OperationalDatabase, Vote);
        var fresh = await host.Client.GetAsync(UnknownPage);

        foreach (var tag in new[] { fresh.Headers.ETag!.Tag, "W/" + fresh.Headers.ETag.Tag, "*", "\"r2-old\"" })
        {
            using var response = await Send(host, tag);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.NotEmpty(await response.Content.ReadAsStringAsync());
        }
    }

    [Fact]
    public async Task ASuccessfulPageWithAMatchingStrongWeakOrWildcardValidatorSkipsItsRender()
    {
        await using var host = await StartAsync();
        await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 3, PublishedAt);
        await ViewsSeed.AddVotesAsync(host.OperationalDatabase, Vote);
        await using var views = NpgsqlDataSource.Create(host.Options.PublicViewsReaderDatabase);
        await using var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        var cache = new PageCache(TestCodeVersion.Sha);
        var responder = new ServerPageResponder(new ActiveReleaseReader(views), cache, new PageRenderer(services, services.GetRequiredService<ILoggerFactory>()));
        foreach (var tag in new[] { cache.ETag(3).ToString(), "W/" + cache.ETag(3), "*", "\"old\", W/" + cache.ETag(3) })
        {
            var context = new DefaultHttpContext();
            context.Request.Headers.IfNoneMatch = tag;
            var response = await responder.RespondAsync<BrokenPage>(context, StatusCodes.Status200OK, new Dictionary<string, object?>(), default);
            Assert.Equal(StatusCodes.Status304NotModified, Assert.IsAssignableFrom<IStatusCodeHttpResult>(response).StatusCode);
            Assert.Equal(cache.ETag(3).ToString(), context.Response.Headers.ETag);
            Assert.Equal(PageCache.CacheControl, context.Response.Headers.CacheControl);
        }
    }

    [Fact]
    public async Task AFailedRenderWithAStaleValidatorLeavesNoCacheHeaders()
    {
        await using var host = await StartAsync();
        await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 3, PublishedAt);
        await ViewsSeed.AddVotesAsync(host.OperationalDatabase, Vote);
        await using var views = NpgsqlDataSource.Create(host.Options.PublicViewsReaderDatabase);
        await using var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        var responder = new ServerPageResponder(new ActiveReleaseReader(views), new PageCache(TestCodeVersion.Sha),
            new PageRenderer(services, services.GetRequiredService<ILoggerFactory>()));
        var context = new DefaultHttpContext();
        context.Request.Headers.IfNoneMatch = "\"stale\"";

        await Assert.ThrowsAsync<InvalidOperationException>(() => responder.RespondAsync<BrokenPage>(context, StatusCodes.Status200OK,
            new Dictionary<string, object?>(), default));

        Assert.False(context.Response.Headers.ContainsKey("ETag"));
        Assert.False(context.Response.Headers.ContainsKey("Cache-Control"));
    }

    [Fact]
    public async Task WithNoActiveReleaseAServerPageAnswers503WithNoCacheValidator()
    {
        await using var host = await StartAsync();

        var response = await host.Client.GetAsync(UnknownPage);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Null(response.Headers.ETag);
        Assert.False(response.Headers.Contains("Cache-Control"));
    }

    [Fact]
    public async Task ThePageHoldsNoReaderDataSoItSetsNoCookieAndDoesNotVaryByCookie()
    {
        await using var host = await StartAsync();
        await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 1, PublishedAt);
        await ViewsSeed.AddVotesAsync(host.OperationalDatabase, Vote);
        using var request = new HttpRequestMessage(HttpMethod.Get, UnknownPage + "?zip=55401");
        request.Headers.Add("Cookie", "session=reader");

        var response = await host.Client.SendAsync(request);
        var html = await response.Content.ReadAsStringAsync();

        Assert.False(response.Headers.Contains("Set-Cookie"));
        Assert.DoesNotContain(response.Headers.Vary, name => name.Equals("Cookie", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain("55401", html);
    }

    [Fact]
    public async Task TheNotFoundPageIsTheStaticNotFoundPagePlusTheEmptyMembersStrip()
    {
        await using var host = await StartAsync();
        await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 1, PublishedAt);
        await ViewsSeed.AddVotesAsync(host.OperationalDatabase, Vote);
        var staticHtml = await new ReleaseRenderer(null, null, BaseUrl).RenderNotFoundAsync(CancellationToken.None);

        var served = await (await host.Client.GetAsync(UnknownPage)).Content.ReadAsStringAsync();

        Assert.DoesNotContain(MembersStripSlot.ElementId, staticHtml);
        Assert.Contains($"id=\"{MembersStripSlot.ElementId}\"", served);
        Assert.Equal(staticHtml, served.Replace(MembersStripHtml, string.Empty));
        Assert.Contains("id=\"site-search\"", served);
    }

    [Fact]
    public async Task WithNoPublicBaseUrlTheServerPageRouteDoesNotExist()
    {
        await using var host = await StartAsync(withBaseUrl: false);
        await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 1, PublishedAt);
        await ViewsSeed.AddVotesAsync(host.OperationalDatabase, Vote);

        var response = await host.Client.GetAsync(UnknownPage);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Null(response.Headers.ETag);
        foreach (var path in PublicAssets.Paths)
        {
            using var asset = await host.Client.GetAsync("/" + path);
            Assert.Equal(HttpStatusCode.NotFound, asset.StatusCode);
            Assert.Null(asset.Headers.ETag);
            Assert.False(asset.Headers.Contains("Cache-Control"));
        }
    }

    [Fact]
    public async Task EverySharedFileIsServedAtItsReleasePathWithTheSavedBytesAndAContentETag()
    {
        await using var host = await StartAsync();

        foreach (var path in PublicAssets.Paths)
        {
            var response = await host.Client.GetAsync("/" + path);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(EmbeddedPublicAssets.Read(path), await response.Content.ReadAsByteArrayAsync());
            Assert.Equal(SharedAssetEndpoints.CacheControl, string.Join(", ", response.Headers.GetValues("Cache-Control")));
            Assert.NotNull(response.Headers.ETag);
        }
        Assert.Equal("font/woff2", (await host.Client.GetAsync("/" + PublicAssets.NewsreaderFont)).Content.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task ASharedFileGets304WhenTheReaderHoldsTheSameBytes()
    {
        await using var host = await StartAsync();
        var first = await host.Client.GetAsync("/" + PublicAssets.Script);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/" + PublicAssets.Script);
        request.Headers.IfNoneMatch.Add(first.Headers.ETag!);

        var second = await host.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotModified, second.StatusCode);
    }

    private const string MembersStripHtml = $"""<div class="members-strip" id="{MembersStripSlot.ElementId}" aria-live="polite"></div>""";

    public sealed class BrokenPage : ComponentBase
    {
        protected override void BuildRenderTree(RenderTreeBuilder builder) => throw new InvalidOperationException("Render failed.");
    }

    private static Task<HttpResponseMessage> Revalidate(ApiHost host, HttpResponseMessage fresh) => Send(host, fresh.Headers.ETag!.Tag);

    private static async Task<HttpResponseMessage> Send(ApiHost host, string tag)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, UnknownPage);
        request.Headers.TryAddWithoutValidation("If-None-Match", tag);
        return await host.Client.SendAsync(request);
    }
}
