using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using CivicLens.PublicApi.ActiveRelease;
using CivicLens.PublicApi.Http;
using CivicLens.PublicApi.Pages;
using CivicLens.PublicWeb.Publication;
using CivicLens.Publication.Contracts;
using CivicLens.Tests.Fixtures;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace CivicLens.Tests.PublicApi.Pages;

public sealed class ServerErrorPageFilterTests : IAsyncDisposable
{
    private static readonly PublicBaseUrl BaseUrl = PublicBaseUrl.Parse("https://civic-lens.test", "test")!;
    private readonly ServiceProvider services = new ServiceCollection().AddLogging().BuildServiceProvider();
    // The failure page reads nothing, so the views source never opens a connection.
    private readonly NpgsqlDataSource views = NpgsqlDataSource.Create("Host=unused.invalid");

    public async ValueTask DisposeAsync()
    {
        await views.DisposeAsync();
        await services.DisposeAsync();
    }

    private ServerErrorPageFilter Filter() => new(
        new ServerPageResponder(new ActiveReleaseReader(views), new PageCache(TestCodeVersion.Sha, BaseUrl), new PageRenderer(services, services.GetRequiredService<ILoggerFactory>())),
        BaseUrl, NullLogger<ServerErrorPageFilter>.Instance);

    private async Task<(DefaultHttpContext Context, object? Result)> InvokeAsync(string path, Func<Task<IResult>> handler)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        var result = await Filter().InvokeAsync(new DefaultEndpointFilterInvocationContext(context), async _ => await handler());
        return (context, result);
    }

    private static IDocument Parse(object? result) => new HtmlParser().ParseDocument(Assert.IsType<ContentHttpResult>(result).ResponseContent!);

    private static Task<IResult> StatementTimeout() =>
        ApiResults.GuardAsync(() => throw new PostgresException("canceling statement due to statement timeout", "ERROR", "ERROR", "57014"));

    [Fact]
    public async Task AStatementTimeoutOfTheBillsListAnswersTheStyledServerErrorPageWith503NoStoreAndLinksHomeAndToBills()
    {
        var (context, result) = await InvokeAsync("/" + PublicationProtocol.ListPagePath(PublishedRecordKind.Action), StatementTimeout);
        var page = Parse(result);

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, ((ContentHttpResult)result!).StatusCode);
        Assert.Equal("text/html; charset=utf-8", ((ContentHttpResult)result).ContentType);
        Assert.Equal(PageCache.FailureCacheControl, context.Response.Headers.CacheControl.ToString());
        Assert.Equal(PublishedServerErrorPage.Title, page.QuerySelector("main .page-header h1")!.TextContent);
        Assert.Equal(PublishedServerErrorPage.Lede, page.QuerySelector("main .page-header-lede")!.TextContent);
        Assert.Equal(["Go to the home page", "Bills"], page.QuerySelectorAll("main .panel a").Select(link => link.TextContent.Trim()));
        Assert.Equal([BaseUrl.Join(PublicationProtocol.HomePath), BaseUrl.Join("bills.html")], page.QuerySelectorAll("main .panel a").Select(link => link.GetAttribute("href")));
        Assert.Equal("noindex", page.QuerySelector("meta[name=robots]")!.GetAttribute("content"));
        Assert.Null(page.QuerySelector("link[rel=canonical]"));
        Assert.Null(page.QuerySelector(".public-freshness"));
        Assert.Equal("Bills", page.QuerySelector(".public-nav a[aria-current=page]")!.TextContent);
    }

    [Fact]
    public async Task AnExceptionOfARecordPageAnswersTheServerErrorPageWith500AndOnlyAHomeLinkBecauseARecordPageHasNoHeaderSection()
    {
        var (context, result) = await InvokeAsync("/records/abc.html", () => throw new InvalidDataException("A view fault."));
        var page = Parse(result);

        Assert.Equal(StatusCodes.Status500InternalServerError, ((ContentHttpResult)result!).StatusCode);
        Assert.Equal(PageCache.FailureCacheControl, context.Response.Headers.CacheControl.ToString());
        Assert.Equal(["Go to the home page"], page.QuerySelectorAll("main .panel a").Select(link => link.TextContent.Trim()));
        Assert.Null(page.QuerySelector(".public-nav a[aria-current]"));
    }

    [Fact]
    public async Task AnAnswerThatIsNotAServerFailurePassesThroughUnchanged()
    {
        var notFound = ApiResults.RecordNotFound();
        var invalid = Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "invalid request");

        Assert.Same(notFound, (await InvokeAsync("/bills.html", () => Task.FromResult(notFound))).Result);
        Assert.Same(invalid, (await InvokeAsync("/bills.html", () => Task.FromResult(invalid))).Result);
    }

    [Theory]
    [InlineData("officials.html", "Officials")]
    [InlineData("officials/a000001.html", "Officials")]
    [InlineData("officials/a000001/record.html", "Officials")]
    [InlineData("issues/taxes.html", "Issues")]
    [InlineData("votes.html", "Votes")]
    [InlineData("boards.html", "Compare parties")]
    [InlineData("records/abc.html", null)]
    [InlineData("index.html", null)]
    [InlineData("officialsx.html", null)]
    public void TheSectionOfAPathIsTheHeaderLinkOfThePageOrOfTheFolderThatHoldsIt(string path, string? section) =>
        Assert.Equal(section, PublicSections.SectionOf(path)?.Text);
}
