using System.Net;
using AngleSharp.Html.Parser;
using CivicLens.PublicApi;
using CivicLens.PublicApi.Pages;
using CivicLens.PublicApi.Votes;
using Microsoft.Extensions.Logging;
using Npgsql;
using CivicLens.Publication.Contracts;
using CivicLens.Tests.Infrastructure.Collection;
using CivicLens.Tests.PublicApi.Officials;

namespace CivicLens.Tests.PublicApi.Votes;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class VotePageEndpointsTests(PostgresCollection postgres)
{
    private static readonly PublicBaseUrl BaseUrl = PublicBaseUrl.Parse("https://civic-lens.test", "test")!;
    private static readonly SeededVote House = new('a', "2026-03-02") { Casts = ["Yea", "Nay", "Nay", "Not Voting"], PersonIds = ["p0", "p1", "p2", "p3"], Issues = ["health"] };
    private static readonly string Path = $"{ServerPageEndpoints.Prefix}/records/{House.RecordId}.html";

    private async Task<ApiHost> StartAsync(bool withTallies = true)
    {
        var host = await ApiHost.StartAsync(postgres, new FakeReleaseSite(), options => options with { PublicBaseUrl = BaseUrl });
        await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 1, new DateTimeOffset(2026, 6, 2, 0, 0, 0, TimeSpan.Zero));
        await ViewsSeed.AddVotesAsync(host.OperationalDatabase, House);
        await OfficialsSeed.AddOfficialsAsync(host.OperationalDatabase,
            new SeededOfficial("p0", "Ann Able"), new SeededOfficial("p1", "Ben Baker"), new SeededOfficial("p2", "Cy Cole"), new SeededOfficial("p3", "Di Dole"));
        await ViewsSeed.ExecuteAsync(host.OperationalDatabase, $"""
            UPDATE vote_member_list_entries SET party = CASE person_id WHEN 'p0' THEN 'Democrat' WHEN 'p1' THEN 'Democrat' ELSE 'Republican' END WHERE list_id = '{House.RecordId}';
            INSERT INTO release_issues (release_number, issue_id, name, definition) VALUES (1, 'health', 'Health care', 'Defined');
            """);
        if (withTallies)
            await ViewsSeed.ExecuteAsync(host.OperationalDatabase, $"""
                UPDATE vote_revisions SET party_tally_count = 2 WHERE record_id = '{House.RecordId}' AND revision_number = 1;
                INSERT INTO vote_revision_party_tallies (record_id, revision_number, party, yea, nay, present, not_voting, other)
                VALUES ('{House.RecordId}', 1, 'Democrat', 1, 1, 0, 0, 0), ('{House.RecordId}', 1, 'Republican', 0, 2, 0, 1, 0);
                """);
        return host;
    }

    private static async Task<AngleSharp.Dom.IDocument> GetPageAsync(ApiHost host, string path, HttpStatusCode expected = HttpStatusCode.OK)
    {
        using var response = await host.Client.GetAsync(path);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == expected, $"{path} gave {(int)response.StatusCode}: {body}");
        return await new HtmlParser().ParseDocumentAsync(body);
    }

    [Fact]
    public async Task TheRouteRendersTheVoteWithItsShellTrailFooterLinkNamesPartyBarsAndIssueChip()
    {
        await using var host = await StartAsync();

        var page = await GetPageAsync(host, Path);

        Assert.Equal("Headline a", page.QuerySelector("h1")!.TextContent);
        Assert.Equal(["Home", "Votes"], page.QuerySelectorAll(".public-breadcrumbs a").Select(link => link.TextContent));
        Assert.Equal("House roll call 1", page.QuerySelector(".public-breadcrumbs [aria-current=page]")!.TextContent);
        Assert.Contains("Report a problem with this vote", page.QuerySelector(".public-footer")!.TextContent);
        Assert.Equal(["House Democrats", "House Republicans"], page.QuerySelectorAll(".vote-parties figcaption").Select(caption => caption.TextContent));
        Assert.Equal("Health care", page.QuerySelector(".vote-chip")!.TextContent);
        Assert.Equal(["Ann Able", "Ben Baker", "Cy Cole", "Di Dole"], page.QuerySelectorAll(".vote-members tbody td:first-child").Select(cell => cell.TextContent));
    }

    [Fact]
    public async Task ARetainedReleaseWithoutPartyCountsShowsNoPartyCardAndNoZeros()
    {
        await using var host = await StartAsync(withTallies: false);

        var page = await GetPageAsync(host, Path);

        Assert.Null(page.QuerySelector(".vote-parties"));
        Assert.NotNull(page.QuerySelector(".vote-parties-unknown"));
        Assert.Single(page.QuerySelectorAll(".party-bar"));
    }

    [Fact]
    public async Task ABlankNameQueryIsNoFilterSoTheFoldStaysClosedAndAllRowsShow()
    {
        await using var host = await StartAsync();

        var page = await GetPageAsync(host, Path + "?q=%20");

        Assert.Null(page.QuerySelector(".vote-members details")!.GetAttribute("open"));
        Assert.Null(page.QuerySelector(".vote-filter-actions a"));
        Assert.Equal(4, page.QuerySelectorAll(".vote-members tbody td:first-child").Length);
    }

    [Fact]
    public async Task TheQueryFiltersTheMemberTableAndALongNameFilterAnswers400()
    {
        await using var host = await StartAsync();

        var nay = await GetPageAsync(host, Path + "?cast=Nay");
        var named = await GetPageAsync(host, Path + "?q=di");
        using var tooLong = await host.Client.GetAsync(Path + "?q=" + new string('x', 300));

        Assert.Equal(["Ben Baker", "Cy Cole"], nay.QuerySelectorAll(".vote-members tbody td:first-child").Select(cell => cell.TextContent));
        Assert.Equal(["Di Dole"], named.QuerySelectorAll(".vote-members tbody td:first-child").Select(cell => cell.TextContent));
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
    }

    [Fact]
    public async Task TheFilterFormAndTheClearLinkPointAtTheRouteThatServedThePage()
    {
        await using var host = await StartAsync();

        var page = await GetPageAsync(host, Path + "?cast=Nay");

        var served = BaseUrl.Join(Path.TrimStart('/'));
        Assert.Equal(served + "#members", page.QuerySelector("form.vote-filter")!.GetAttribute("action"));
        Assert.Equal(served + "#members", page.QuerySelector(".vote-filter-actions a")!.GetAttribute("href"));
    }

    [Fact]
    public async Task TheReaderBuildsThePageOnOneConnectionAndTheMemberReadSkipsTheSourceTexts()
    {
        await using var host = await StartAsync();
        var connections = 0;
        var commands = new CommandLog();
        var views = new NpgsqlConnectionStringBuilder(host.Options.PublicViewsDatabase) { Pooling = false }.ConnectionString;
        await using var source = new NpgsqlDataSourceBuilder(views)
            .UseLoggerFactory(new CommandLogFactory(commands)).EnableParameterLogging()
            .UsePhysicalConnectionInitializer(_ => connections++, _ => { connections++; return Task.CompletedTask; }).Build();
        var reader = new VotePageReader(new VoteStore(source), source);

        var page = await reader.ReadAsync(House.RecordId, CancellationToken.None);

        Assert.Equal(1, connections);
        var memberRead = Assert.Single(commands.Lines, line => line.StartsWith("Executing", StringComparison.Ordinal) && line.Contains("selected AS MATERIALIZED", StringComparison.Ordinal));
        Assert.EndsWith(", False]", memberRead.TrimEnd());  // The last parameter is with_sources: the member read skips the source texts.
        Assert.Equal(["Ann Able", "Ben Baker", "Cy Cole", "Di Dole"], page!.Members.Select(member => member.Name));
        Assert.Equal("Health care", Assert.Single(page.Issues).Name);
        Assert.NotEmpty(page.Sources);
        Assert.Null(await reader.ReadAsync(new string('f', 64), CancellationToken.None));
    }

    [Fact]
    public async Task AnUnknownOrMalformedRecordIdAnswersThe404Page()
    {
        await using var host = await StartAsync();

        await GetPageAsync(host, $"{ServerPageEndpoints.Prefix}/records/{new string('f', 64)}.html", HttpStatusCode.NotFound);
        await GetPageAsync(host, $"{ServerPageEndpoints.Prefix}/records/not-a-record.html", HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ASuccessfulPageAnswers304ForItsOwnReleaseETag()
    {
        await using var host = await StartAsync();
        using var fresh = await host.Client.GetAsync(Path);
        using var request = new HttpRequestMessage(HttpMethod.Get, Path);
        request.Headers.IfNoneMatch.Add(fresh.Headers.ETag!);

        using var revalidated = await host.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, fresh.StatusCode);
        Assert.Equal(HttpStatusCode.NotModified, revalidated.StatusCode);
    }

    private sealed class CommandLog
    {
        public List<string> Lines { get; } = [];
    }

    private sealed class CommandLogFactory(CommandLog log) : ILoggerFactory, ILogger
    {
        public ILogger CreateLogger(string categoryName) => this;
        public void AddProvider(ILoggerProvider provider) { }
        public bool IsEnabled(LogLevel logLevel) => true;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public void Dispose() { }
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            log.Lines.Add(formatter(state, exception));
    }
}
