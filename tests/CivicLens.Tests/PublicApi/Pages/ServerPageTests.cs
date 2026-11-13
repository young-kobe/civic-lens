using System.Net;
using AngleSharp.Html.Parser;
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
    private const string VoteKindsRoute = ServerPageEndpoints.Prefix + "/" + PublicationProtocol.VoteKindsPagePath;
    private static readonly PublicBaseUrl BaseUrl = PublicBaseUrl.Parse("https://civic-lens.test", "test")!;
    private static readonly SeededVote Vote = new('e', "2026-03-03");
    private static readonly DateTimeOffset PublishedAt = new(2026, 6, 9, 12, 0, 0, TimeSpan.Zero);

    private Task<ApiHost> StartAsync(bool withBaseUrl = true) =>
        ApiHost.StartAsync(postgres, options => options with { PublicBaseUrl = withBaseUrl ? BaseUrl : null });

    private Task<ApiHost> StartWithBrowserApiAsync() =>
        ApiHost.StartAsync(postgres, options => options with
        {
            PublicBaseUrl = BaseUrl,
            BrowserApiUrl = PublicBaseUrl.Parse("https://api.civic-lens.test", PublicApiOptions.BrowserApiUrlVariable)
        });

    [Fact]
    public async Task AnUnknownServerPageAnswers404WithTheETagOfTheActiveReleaseAndBuild()
    {
        await using var host = await StartAsync();
        await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 7, PublishedAt);
        await ViewsSeed.AddVotesAsync(host.OperationalDatabase, Vote);

        var response = await host.Client.GetAsync(UnknownPage);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.True(response.Headers.ETag!.IsWeak);
        Assert.Matches($"^W/\"r7-{BuildId.FromAssembly(typeof(PublicApiApp).Assembly)}-[0-9a-f]{{64}}\"$", response.Headers.ETag.ToString());
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

        Assert.Equal(PageCache.CacheControl, string.Join(", ", fresh.Headers.GetValues("Cache-Control")));
        Assert.False(fresh.Headers.CacheControl!.NoTransform);
        Assert.Equal(string.Join(", ", fresh.Headers.GetValues("Cache-Control")), string.Join(", ", revalidated.Headers.GetValues("Cache-Control")));
    }

    [Fact]
    public async Task ANotFoundPageIgnoresMatchingWeakAndWildcardValidatorsAndKeepsIts404Body()
    {
        await using var host = await StartAsync();
        await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 3, PublishedAt);
        await ViewsSeed.AddVotesAsync(host.OperationalDatabase, Vote);
        var fresh = await host.Client.GetAsync(UnknownPage);

        foreach (var tag in new[] { fresh.Headers.ETag!.ToString(), fresh.Headers.ETag.Tag, "*", "\"r2-old\"" })
        {
            using var response = await Send(host, tag);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.NotEmpty(await response.Content.ReadAsStringAsync());
        }
    }

    [Fact]
    public async Task TheSuppliedSnapshotReleaseControlsSuccessfulRevalidationWithoutAnotherReleaseRead()
    {
        await using var host = await StartAsync();
        await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 3, PublishedAt);
        await ViewsSeed.AddVotesAsync(host.OperationalDatabase, Vote);
        await using var views = NpgsqlDataSource.Create(host.Options.PublicViewsReaderDatabase);
        await using var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        var cache = new PageCache(TestCodeVersion.Sha, BaseUrl);
        var responder = new ServerPageResponder(new ActiveReleaseReader(views), cache, new PageRenderer(services, services.GetRequiredService<ILoggerFactory>()));
        foreach (var tag in new[] { cache.ETag(9).ToString(), cache.ETag(9).Tag.ToString(), "*", "\"old\", " + cache.ETag(9) })
        {
            var context = new DefaultHttpContext();
            context.Request.Headers.IfNoneMatch = tag;
            var response = await responder.RespondAsync<BrokenPage>(context, StatusCodes.Status200OK, 9, new Dictionary<string, object?>(), default);
            Assert.Equal(StatusCodes.Status304NotModified, Assert.IsAssignableFrom<IStatusCodeHttpResult>(response).StatusCode);
            Assert.Equal(cache.ETag(9).ToString(), context.Response.Headers.ETag);
            Assert.Equal(PageCache.CacheControl, context.Response.Headers.CacheControl);
        }
    }

    [Fact]
    public async Task AHeldFreshnessAnswers304WithoutReadingTheLazyParameters()
    {
        await using var views = NpgsqlDataSource.Create("Host=unused.invalid");
        await using var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        var cache = new PageCache(TestCodeVersion.Sha, BaseUrl);
        var responder = new ServerPageResponder(new ActiveReleaseReader(views), cache, new PageRenderer(services, services.GetRequiredService<ILoggerFactory>()));
        var context = new DefaultHttpContext();
        context.Request.Headers.IfNoneMatch = cache.ETag(9).ToString();
        var reads = 0;

        var response = await responder.RespondAsync<BrokenPage>(context, StatusCodes.Status200OK, new ReleaseFreshness(9, null, null), () =>
        {
            reads++;
            return Task.FromResult<IReadOnlyDictionary<string, object?>>(new Dictionary<string, object?>());
        }, default);

        Assert.Equal(StatusCodes.Status304NotModified, Assert.IsAssignableFrom<IStatusCodeHttpResult>(response).StatusCode);
        Assert.Equal(0, reads);
    }

    [Fact]
    public async Task APageOfAnEarlierReleaseShowsNoFreshnessLineInsteadOfTheDaysOfTheActiveRelease()
    {
        await using var host = await StartAsync();
        await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 4, PublishedAt);
        await ViewsSeed.AddVotesAsync(host.OperationalDatabase, Vote);
        await using var views = NpgsqlDataSource.Create(host.Options.PublicViewsReaderDatabase);
        await using var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        var cache = new PageCache(TestCodeVersion.Sha, BaseUrl);
        var responder = new ServerPageResponder(new ActiveReleaseReader(views), cache,
            new PageRenderer(services, services.GetRequiredService<ILoggerFactory>()));
        var parameters = new Dictionary<string, object?> { [nameof(PublishedNotFoundPage.RootPath)] = "https://civic-lens.test/" };
        var earlierContext = new DefaultHttpContext();

        var same = await responder.RespondAsync<PublishedNotFoundPage>(new DefaultHttpContext(), StatusCodes.Status404NotFound, 4, parameters, default);
        var earlier = await responder.RespondAsync<PublishedNotFoundPage>(earlierContext, StatusCodes.Status404NotFound, 3, parameters, default);

        Assert.Contains("public-freshness", Assert.IsType<Microsoft.AspNetCore.Http.HttpResults.ContentHttpResult>(same).ResponseContent);
        Assert.DoesNotContain("public-freshness", Assert.IsType<Microsoft.AspNetCore.Http.HttpResults.ContentHttpResult>(earlier).ResponseContent);
        Assert.Equal(cache.ETag(3).ToString(), earlierContext.Response.Headers.ETag);
    }

    [Fact]
    public async Task AFailedRenderWithAStaleValidatorLeavesNoCacheHeaders()
    {
        await using var host = await StartAsync();
        await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 3, PublishedAt);
        await ViewsSeed.AddVotesAsync(host.OperationalDatabase, Vote);
        await using var views = NpgsqlDataSource.Create(host.Options.PublicViewsReaderDatabase);
        await using var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        var responder = new ServerPageResponder(new ActiveReleaseReader(views), new PageCache(TestCodeVersion.Sha, BaseUrl),
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

        foreach (var path in new[] { UnknownPage, ServerPageEndpoints.Prefix + "/compare", VoteKindsRoute })
        {
            using var response = await host.Client.GetAsync(path);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.Null(response.Headers.ETag);
            Assert.False(response.Headers.Contains("Cache-Control"));
        }
    }

    [Fact]
    public async Task TheHomeRouteUsesTheHomeSnapshotReleaseAndAnswers503WithoutCacheWhenItIsMissing()
    {
        await using var host = await StartAsync();
        var empty = await host.Client.GetAsync(ServerPageEndpoints.Prefix);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, empty.StatusCode);
        Assert.Null(empty.Headers.ETag);
        Assert.False(empty.Headers.Contains("Cache-Control"));

        await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 9, PublishedAt);
        await ViewsSeed.AddVotesAsync(host.OperationalDatabase, Vote);
        using var request = new HttpRequestMessage(HttpMethod.Get, ServerPageEndpoints.Prefix + "?zip=55401");
        request.Headers.Add("Cookie", "session=reader");
        var response = await host.Client.SendAsync(request);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.ETag!.IsWeak);
        Assert.Contains("W/\"r9-", response.Headers.ETag.ToString());
        Assert.False(response.Headers.CacheControl!.NoTransform);
        Assert.Contains("Headline e", html);
        Assert.Contains("Summary e", html);
        Assert.DoesNotContain("data-zip-api", html);
        Assert.DoesNotContain("55401", html);
        Assert.False(response.Headers.Contains("Set-Cookie"));
        Assert.DoesNotContain(response.Headers.Vary, name => name.Equals("Cookie", StringComparison.OrdinalIgnoreCase));

        using var revalidation = new HttpRequestMessage(HttpMethod.Get, ServerPageEndpoints.Prefix + "/");
        revalidation.Headers.IfNoneMatch.Add(response.Headers.ETag!);
        using var cached = await host.Client.SendAsync(revalidation);
        Assert.Equal(HttpStatusCode.NotModified, cached.StatusCode);
        Assert.False(cached.Headers.CacheControl!.NoTransform);
        Assert.Equal(response.Headers.ETag, cached.Headers.ETag);
        Assert.Equal(response.Headers.CacheControl, cached.Headers.CacheControl);
    }

    [Theory]
    [InlineData("Model", PublishedRecordLabels.ModelWording)]
    [InlineData("Code", PublishedRecordLabels.CodeWording)]
    [InlineData("Person", null)]
    public async Task RecentHeadlinesKeepTheirOwnPublishedWordingLabel(string origin, string? expectedLabel)
    {
        await using var host = await StartAsync();
        await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 4, PublishedAt);
        await ViewsSeed.AddVotesAsync(host.OperationalDatabase, Vote,
            new SeededVote('f', "2026-03-02") { Kind = "Amendment", TextOrigin = origin });

        var response = await host.Client.GetAsync(ServerPageEndpoints.Prefix);
        var page = await new HtmlParser().ParseDocumentAsync(await response.Content.ReadAsStringAsync());
        var row = Assert.Single(page.QuerySelectorAll(".record-rows > li"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Headline f", row.TextContent);
        if (expectedLabel is not null) Assert.Contains(expectedLabel, row.TextContent);
        else
        {
            Assert.DoesNotContain(PublishedRecordLabels.ModelWording, row.TextContent);
            Assert.DoesNotContain(PublishedRecordLabels.CodeWording, row.TextContent);
        }
    }

    [Fact]
    public async Task TheHomePreviewDoesNotClaimTheLiveRootRoute()
    {
        await using var host = await StartAsync();
        await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 4, PublishedAt);
        await ViewsSeed.AddVotesAsync(host.OperationalDatabase, Vote);

        using var response = await host.Client.GetAsync("/");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Null(response.Headers.ETag);
    }

    [Fact]
    public async Task TheHomeRouteEmbedsTheConfiguredBrowserApiOriginForTheExistingZipFlow()
    {
        await using var host = await StartWithBrowserApiAsync();
        await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 4, PublishedAt);
        await ViewsSeed.AddVotesAsync(host.OperationalDatabase, Vote);

        var html = await (await host.Client.GetAsync(ServerPageEndpoints.Prefix)).Content.ReadAsStringAsync();

        Assert.Contains("data-zip-api=\"https://api.civic-lens.test\"", html);
        Assert.Contains("Find my members", html);
        Assert.Contains("browse all officials", html);
    }

    [Fact]
    public async Task ComparePageIsPublicAndQueryFreeWhileItsFormUsesTheConfiguredApiOrigin()
    {
        await using var host = await StartWithBrowserApiAsync();
        await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 4, PublishedAt);
        await ViewsSeed.AddVotesAsync(host.OperationalDatabase, Vote);
        using var response = await host.Client.GetAsync("/web/compare?zip=55401");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("https://civic-lens.test/compare.html", Parse(html).QuerySelector("link[rel=canonical]")!.GetAttribute("href"));
        Assert.Contains("data-zip-api=\"https://api.civic-lens.test\"", html);
        Assert.Contains("data-zip-view=\"compare\"", html);
        Assert.DoesNotContain("55401", html);
        Assert.Equal(PageCache.CacheControl, response.Headers.CacheControl?.ToString());
        Assert.NotNull(response.Headers.ETag);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/web/compare");
        request.Headers.IfNoneMatch.Add(response.Headers.ETag!);
        using var revalidated = await host.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.NotModified, revalidated.StatusCode);
    }

    [Fact]
    public async Task TheVoteKindIndexIsServedWithItsCanonicalPathAndTheReleaseETagAndRevalidatesTo304()
    {
        await using var host = await StartAsync();
        await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 4, PublishedAt);

        using var response = await host.Client.GetAsync(VoteKindsRoute);
        var page = Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("https://civic-lens.test/" + PublicationProtocol.VoteKindsPagePath, page.QuerySelector("link[rel=canonical]")!.GetAttribute("href"));
        Assert.Equal(VoteMeaning.Kinds.Count + 1, page.QuerySelectorAll("tbody tr").Length);
        Assert.Contains("r4-", response.Headers.ETag!.ToString());
        Assert.Equal(PageCache.CacheControl, response.Headers.CacheControl?.ToString());

        using var request = new HttpRequestMessage(HttpMethod.Get, VoteKindsRoute);
        request.Headers.IfNoneMatch.Add(response.Headers.ETag!);
        using var revalidated = await host.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.NotModified, revalidated.StatusCode);
    }

    [Theory]
    [InlineData("/web/vote-kinds")]
    [InlineData("/web/vote-kinds.html/extra")]
    [InlineData("/web/Vote-Kinds.htm")]
    public async Task ABadVoteKindIndexPathAnswers404ThroughTheSharedNotFoundPage(string path)
    {
        await using var host = await StartAsync();
        await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 4, PublishedAt);

        using var response = await host.Client.GetAsync(path);
        var page = Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Page not found", page.QuerySelector(".empty h3")!.TextContent.Trim());
        Assert.Empty(page.QuerySelectorAll("tbody tr"));
    }

    [Fact]
    public async Task ComparePageNeedsOnlyReleaseIdentityEvenWhenTheReleaseIsEmpty()
    {
        await using var host = await StartWithBrowserApiAsync();
        await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 4, PublishedAt);
        await ViewsSeed.ExecuteAsync(host.OperationalDatabase, "REVOKE SELECT ON public_api.bills FROM civic_lens_public");

        using var response = await host.Client.GetAsync("/web/compare");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("r4-", response.Headers.ETag!.ToString());
        Assert.Empty(Parse(await response.Content.ReadAsStringAsync()).QuerySelectorAll(".zip-member-card"));
    }

    [Fact]
    public async Task ComparePageKeepsItsBrowseRouteWhenBrowserApiIsNotConfigured()
    {
        await using var host = await StartAsync();
        await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 4, PublishedAt);
        await ViewsSeed.AddVotesAsync(host.OperationalDatabase, Vote);

        var html = await host.Client.GetStringAsync("/web/compare");
        var page = Parse(html);

        Assert.Equal("Compare your members", page.QuerySelector("h1")!.TextContent);
        Assert.Null(page.QuerySelector("[data-zip-api]"));
        Assert.Equal(BaseUrl.Join(PublicationProtocol.FacetsPath), page.QuerySelectorAll(".public-nav a").Single(link => link.TextContent == "Issues").GetAttribute("href"));
        Assert.Equal("https://civic-lens.test/officials.html", page.QuerySelector(".home-browse a")!.GetAttribute("href"));
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
        Assert.Equal(staticHtml, ServerShellOnly(served));
        Assert.Contains("id=\"site-search\"", served);
    }

    [Fact]
    public async Task WithNoPublicBaseUrlTheServerPageRouteDoesNotExist()
    {
        await using var host = await StartAsync(withBaseUrl: false);
        await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 1, PublishedAt);
        await ViewsSeed.AddVotesAsync(host.OperationalDatabase, Vote);

        var response = await host.Client.GetAsync(UnknownPage);
        var home = await host.Client.GetAsync(ServerPageEndpoints.Prefix);
        using var compare = await host.Client.GetAsync(ServerPageEndpoints.Prefix + "/compare");
        using var kinds = await host.Client.GetAsync(VoteKindsRoute);

        Assert.Equal(HttpStatusCode.NotFound, compare.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, kinds.StatusCode);
        Assert.Null(compare.Headers.ETag);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, home.StatusCode);
        Assert.Null(response.Headers.ETag);
        Assert.Null(home.Headers.ETag);
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

    private static readonly string MembersStripHtml = $"""<div class="members-strip" id="{MembersStripSlot.ElementId}" data-officials-page="{BaseUrl.Join(PublicationProtocol.OfficialsPagePath)}" aria-live="polite"></div>""";

    public sealed class BrokenPage : ComponentBase
    {
        protected override void BuildRenderTree(RenderTreeBuilder builder) => throw new InvalidOperationException("Render failed.");
    }

    private static Task<HttpResponseMessage> Revalidate(ApiHost host, HttpResponseMessage fresh) => Send(host, fresh.Headers.ETag!.Tag);

    /// <summary>The served page without the three things that only a server page adds: the members strip, the freshness line, and the preview Issues link.</summary>
    /// <summary>The served page with the parts that only a server page has put back to the static form: the members strip, the freshness line,
    /// and the preview header links of the issues index and the three kind lists, which the static header points at the issues page and the search page.</summary>
    private static string ServerShellOnly(string served)
    {
        var staticHeader = PublicSections.RecordLists.Where(list => list.Kind is PublishedRecordKind.Vote or PublishedRecordKind.Action or PublishedRecordKind.ExecutiveAction)
            .Aggregate(served.Replace(MembersStripHtml, string.Empty).Replace(PublicSections.PreviewIssuesPath, BaseUrl.Join(PublicationProtocol.FacetsPath)),
                (html, list) => html.Replace($"\"{list.Href}\"", $"\"{BaseUrl.Join(PublicationProtocol.SearchByKindPath(list.Kind))}\""));
        return System.Text.RegularExpressions.Regex.Replace(staticHeader, "<p class=\"public-freshness\">.*?</p>", string.Empty, System.Text.RegularExpressions.RegexOptions.Singleline);
    }

    private static AngleSharp.Dom.IDocument Parse(string html) => new AngleSharp.Html.Parser.HtmlParser().ParseDocument(html);

    private static async Task<HttpResponseMessage> Send(ApiHost host, string tag)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, UnknownPage);
        request.Headers.TryAddWithoutValidation("If-None-Match", tag);
        return await host.Client.SendAsync(request);
    }
}
