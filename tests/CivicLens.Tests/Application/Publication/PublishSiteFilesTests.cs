using System.Text;
using System.Xml.Linq;
using CivicLens.Application.Publication;
using CivicLens.Publication.Contracts;

namespace CivicLens.Tests.Application.Publication;

public sealed class PublishSiteFilesTests
{
    private const string Base = "https://civic.example";
    private const int IdPool = PublishedCatalog.MaximumEntries - 10;
    private static readonly XNamespace Sitemap = "http://www.sitemaps.org/schemas/sitemap/0.9";

    [Fact]
    public async Task TheSitemapListsEveryPageOfTheReleaseSoASearchEngineFindsPagesNoLinkReaches()
    {
        var scenario = new PublicationScenario { BaseUrl = PublicBaseUrl.Parse(Base, "V") };
        var first = await PublishAsync(scenario, "k1", Statement(scenario.AddStatement('a')), Statement(scenario.AddStatement('b', speakerIds: ["chair"])));

        var second = await PublishAsync(scenario, "k2", Statement(scenario.AddStatement('c', speakerIds: ["chair"])));

        var urls = Urls(scenario, second.DirectoryName);
        Assert.Contains((first.DirectoryName, PublicationProtocol.OfficialPagePath("mayor")), scenario.Releases.Links);
        Assert.Equal(
        [
            $"{Base}/", $"{Base}/about.html", $"{Base}/all.html", $"{Base}/briefs.html", $"{Base}/glossary.html", $"{Base}/issues.html", $"{Base}/issues/housing.html", $"{Base}/method.html",
            $"{Base}/officials.html", $"{Base}/officials/chair.html", $"{Base}/officials/mayor.html",
            $"{Base}/policy-areas/land-use.html",
            .. new[] { 'a', 'b', 'c' }.Select(id => $"{Base}/records/{new string(id, 64)}.html")
        ], urls);
    }

    [Fact]
    public async Task TheSitemapLeavesOutSearchReportTheNotFoundPageAndWithdrawnNoticesBecauseReadersShouldNotLandOnThem()
    {
        var scenario = new PublicationScenario { BaseUrl = PublicBaseUrl.Parse(Base, "V") };
        var action = scenario.AddAction('a');
        var statement = scenario.AddStatement('b');
        await PublishAsync(scenario, "k1", new(PublishedRecordKind.Action, action), Statement(statement));

        var second = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner,
            new PublishReleaseRequest([], "k2") { Withdrawals = [new(PublishedRecordKind.Action, action, "Wrong bill.")] });

        var files = scenario.Releases.Directories[second.DirectoryName];
        Assert.Contains(PublicationProtocol.RecordPagePath(action), files.Keys);
        var urls = Urls(scenario, second.DirectoryName);
        Assert.Contains($"{Base}/records/{statement}.html", urls);
        Assert.DoesNotContain(urls, url => url.EndsWith($"/{PublicationProtocol.SearchPagePath}", StringComparison.Ordinal) || url.EndsWith($"/{PublicationProtocol.ReportPagePath}", StringComparison.Ordinal)
            || url.EndsWith($"/{PublicationProtocol.NotFoundPagePath}", StringComparison.Ordinal) || url.Contains(action, StringComparison.Ordinal));
        Assert.DoesNotContain(urls, url => url.Contains('?', StringComparison.Ordinal));
    }

    [Fact]
    public async Task TheSitemapListsAFacetPageBeyondTheFirstAndAPastCongressPageOfAnOfficialSoSearchEnginesFindDeepPages()
    {
        var scenario = new PublicationScenario { BaseUrl = PublicBaseUrl.Parse(Base, "V") };
        scenario.SeedRelease(withFiles: true, [.. Enumerable.Range(0, PublicationProtocol.PageSize + 1).Select(index => new PublishedRecordEntry
        {
            Kind = PublishedRecordKind.DocumentChange,
            Officials = [],
            RecordId = index.ToString("x32"),
            RevisionNumber = 1,
            Headline = "Seeded",
            FirstPublishedAtUtc = PublicationScenario.ApprovedAt.AddMinutes(-index - 10),
            IssueIds = ["housing"],
            PolicyAreaIds = []
        })]);
        var older = scenario.AddVote('a', congress: 118);
        var newer = scenario.AddVote('b');

        var summary = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k",
            new PublishedRecordReference(PublishedRecordKind.Vote, older), new PublishedRecordReference(PublishedRecordKind.Vote, newer)));

        var urls = Urls(scenario, summary.DirectoryName);
        Assert.Contains($"{Base}/{PublicationProtocol.IssuePagePath("housing", 2)}", urls);
        Assert.Contains($"{Base}/{PublicationProtocol.OfficialCongressPagePath("mayor", 118)}", urls);
    }

    [Fact]
    public async Task TheSitemapAddressesAreSortedSoTheSameReleaseAlwaysGivesTheSameFile()
    {
        var text = await SitemapTextAsync('b', 'a', 'c');

        var locations = text.Split('\n').Where(line => line.StartsWith("<url>", StringComparison.Ordinal)).ToList();
        Assert.Equal(locations.Order(StringComparer.Ordinal), locations);
    }

    [Fact]
    public async Task ARobotsFileAllowsEveryoneAndPointsToTheSitemap()
    {
        var scenario = new PublicationScenario { BaseUrl = PublicBaseUrl.Parse(Base + "/", "V") };

        var summary = await PublishAsync(scenario, "k", Statement(scenario.AddStatement('a')));

        Assert.Equal($"User-agent: *\nAllow: /\nSitemap: {Base}/{PublicationProtocol.SitemapPath}\n", Text(scenario, summary.DirectoryName, PublicationProtocol.RobotsPath));
        Assert.Equal("<html>not found</html>", Text(scenario, summary.DirectoryName, PublicationProtocol.NotFoundPagePath));
    }

    [Fact]
    public async Task WithoutABaseUrlTheReleaseHasNoSitemapRobotsFileOrNotFoundPageSoItStaysAsBefore()
    {
        var scenario = new PublicationScenario();

        var summary = await PublishAsync(scenario, "k", Statement(scenario.AddStatement('a')));

        var files = scenario.Releases.Directories[summary.DirectoryName].Keys;
        Assert.DoesNotContain(files, path => path is PublicationProtocol.SitemapPath or PublicationProtocol.RobotsPath or PublicationProtocol.NotFoundPagePath);
    }

    [Fact]
    public async Task ThePublishFailsInsteadOfWritingASitemapThatSearchEnginesRejectForHavingTooManyAddresses()
    {
        var scenario = new PublicationScenario { BaseUrl = PublicBaseUrl.Parse(Base, "V") };
        scenario.SeedRelease(withFiles: true, [.. Enumerable.Range(0, PublicationProtocol.MaximumRecordsPerRelease - 1).Select(index => new PublishedRecordEntry
        {
            Kind = PublishedRecordKind.DocumentChange,
            Officials = [],
            RecordId = index.ToString("x32"),
            RevisionNumber = 1,
            Headline = "Seeded",
            FirstPublishedAtUtc = PublicationScenario.ApprovedAt.AddMinutes(-index - 10),
            IssueIds = [.. Enumerable.Range(0, 32).Select(offset => $"i{(index * 32 + offset) % IdPool}")],
            PolicyAreaIds = [.. Enumerable.Range(0, 32).Select(offset => $"p{(index * 32 + offset) % IdPool}")]
        })]);
        var draft = scenario.AddDraft('a');

        var failure = await Assert.ThrowsAsync<ArgumentException>(() => scenario.Publisher().ExecuteAsync(PublicationScenario.Owner,
            PublicationScenario.Request("k", draft)));

        Assert.Contains("sitemap", failure.Message, StringComparison.Ordinal);
        Assert.Equal(1, scenario.Releases.DiscardedStagings);
    }

    private static async Task<string> SitemapTextAsync(params char[] ids)
    {
        var scenario = new PublicationScenario { BaseUrl = PublicBaseUrl.Parse(Base, "V") };
        var summary = await PublishAsync(scenario, "k", [.. ids.Select(id => Statement(scenario.AddStatement(id)))]);
        return Text(scenario, summary.DirectoryName, PublicationProtocol.SitemapPath);
    }

    private static PublishedRecordReference Statement(string id) => new(PublishedRecordKind.Statement, id);

    private static Task<PublicationReleaseSummary> PublishAsync(PublicationScenario scenario, string key, params PublishedRecordReference[] records) =>
        scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request(key, records));

    private static string[] Urls(PublicationScenario scenario, string directory) =>
        [.. XDocument.Parse(Text(scenario, directory, PublicationProtocol.SitemapPath)).Root!.Elements(Sitemap + "url").Select(url => url.Element(Sitemap + "loc")!.Value)];

    private static string Text(PublicationScenario scenario, string directory, string path) =>
        Encoding.UTF8.GetString(scenario.Releases.Directories[directory][path]);
}
