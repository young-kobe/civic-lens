using System.Net;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using CivicLens.PublicApi;
using CivicLens.PublicApi.Pages;
using CivicLens.Publication.Contracts;
using CivicLens.Tests.Fixtures;
using CivicLens.Tests.Infrastructure.Collection;
using CivicLens.Tests.PublicApi.DocumentChanges;
using CivicLens.Tests.PublicApi.Officials;
using CivicLens.Tests.PublicApi.Statements;
using CivicLens.Tests.PublicApi.Votes;

namespace CivicLens.Tests.PublicApi.Pages;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class RecordPageEndpointsTests(PostgresCollection postgres)
{
    private static readonly PublicBaseUrl BaseUrl = PublicBaseUrl.Parse("https://civic-lens.test", "test")!;
    private static readonly SeededStatement Statement = new('a', "2026-03-02", ["official-z", "official-a"], ["health", "unlisted"], "Analysis", "Code");
    private static readonly string ActionId = new('e', 64);
    private const string Source = "Intro. Ann said \"exact quote\" on Friday. End.";
    private const string ExtractionId = "1111111111111111111111111111111111111111111111111111111111111111";

    private Task<ApiHost> StartAsync() => ApiHost.StartAsync(postgres, new FakeReleaseSite(), options => options with { PublicBaseUrl = BaseUrl });

    private static async Task<IDocument> GetPageAsync(ApiHost host, string path, HttpStatusCode expected = HttpStatusCode.OK)
    {
        using var response = await host.Client.GetAsync(path);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == expected, $"{path} gave {(int)response.StatusCode}: {body}");
        return await new HtmlParser().ParseDocumentAsync(body);
    }

    private static string Path(string kind, string id) => $"{ServerPageEndpoints.Prefix}/{kind}/{id}";

    [Fact]
    public async Task TheStatementRouteRendersTheSavedQuoteWithOrderedSpeakerNamesAndIssueNames()
    {
        await using var host = await StartAsync();
        await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 1, new DateTimeOffset(2026, 6, 2, 0, 0, 0, TimeSpan.Zero));
        await StatementsSeed.AddAsync(host.OperationalDatabase, Statement);
        await StatementEndpointsTests.AddDetailEvidenceAsync(host.OperationalDatabase, Statement.RecordId, ExtractionId, Source,
            Source.IndexOf("exact quote", StringComparison.Ordinal), "exact quote".Length, Source.IndexOf("Friday", StringComparison.Ordinal), 6);
        await OfficialsSeed.AddOfficialsAsync(host.OperationalDatabase, new SeededOfficial("official-a", "Ann Able"), new SeededOfficial("official-z", "Zed Zane"));
        await ViewsSeed.ExecuteAsync(host.OperationalDatabase, "INSERT INTO release_issues (release_number, issue_id, name, definition) VALUES (1, 'health', 'Health care', 'Defined');");

        var page = await GetPageAsync(host, Path(RecordPageEndpoints.StatementPath, Statement.RecordId));

        Assert.Equal("Headline a", page.QuerySelector("h1")!.TextContent);
        Assert.Equal("Zed Zane, Ann Able", page.QuerySelector(".public-breadcrumbs [aria-current=page]")!.TextContent);
        Assert.Equal(["Zed Zane", "Ann Able"], page.QuerySelectorAll(".detail-list a").Select(link => link.TextContent));
        Assert.Equal("Health care", page.QuerySelector(".link-chip")!.TextContent);
        Assert.Equal(["Health care", "unlisted"], page.QuerySelectorAll(".detail-chips li").Select(item => item.TextContent.Trim()));
        Assert.Equal("exact quote", page.QuerySelector(".detail-quotes blockquote")!.TextContent);
        Assert.Equal(["exact quote", "Friday"], page.QuerySelectorAll("#source mark").Select(mark => mark.TextContent));
        Assert.Contains("Said on 2 Mar 2026.", page.QuerySelector(".detail-lede")!.TextContent);
        Assert.NotNull(page.QuerySelector(".ai-summary-label"));
    }

    [Fact]
    public async Task TheExecutiveActionRouteRendersItsFactsAndNoticesInPositionOrder()
    {
        await using var host = await StartAsync();
        await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 1, new DateTimeOffset(2026, 6, 2, 0, 0, 0, TimeSpan.Zero));
        await ViewsSeed.ExecuteAsync(host.OperationalDatabase, "SET session_replication_role = replica;\n" + PublicRecordPageRows.ExecutiveAction(ActionId));

        var page = await GetPageAsync(host, Path(RecordPageEndpoints.ExecutiveActionPath, ActionId));

        Assert.Equal("Title two", page.QuerySelector("h1")!.TextContent);
        Assert.Contains("Signed by President Jane Roe on 20 Jan 2026. Printed in the Federal Register on 23 Jan 2026, citation 91 FR 1.", page.QuerySelector(".detail-lede")!.TextContent);
        Assert.Equal(["https://fr.example/9", "https://fr.example/5"],
            page.QuerySelectorAll(".detail-facts dd a").Where(link => link.TextContent == "Open the correction").Select(link => link.GetAttribute("href")!));
        Assert.Equal("Executive actions", page.QuerySelectorAll(".public-breadcrumbs a")[1].TextContent);
    }

    [Fact]
    public async Task TheDocumentChangeRouteRendersTheRedlineFromTheExactStoredOffsets()
    {
        await using var host = await StartAsync();
        var created = await DocumentChangeEndpointsTests.CreateDraftAsync(host, "page", "Book a tour seven days before.", "Book a tour three days before.");
        var saved = await DocumentChangeEndpointsTests.SaveAsync(host, created, "Headline", created.Citations);
        await DocumentChangeEndpointsTests.ApproveAsync(host, saved);
        await DocumentChangeEndpointsTests.CommitAsync(host, 1, null, saved);

        var page = await GetPageAsync(host, Path(RecordPageEndpoints.DocumentChangePath, saved.DraftId));

        Assert.Equal("Headline", page.QuerySelector("h1")!.TextContent);
        Assert.Equal("seven", page.QuerySelector(".redline del")!.TextContent.Replace("Removed: ", ""));
        Assert.Equal("three", page.QuerySelector(".redline ins")!.TextContent.Replace("Added: ", ""));
        Assert.Equal(["Book a tour seven days before.", "Book a tour three days before."], page.QuerySelectorAll("#source .doc-text").Select(text => text.TextContent));
    }

    [Fact]
    public async Task ARecordOfAnotherKindAMalformedIdAndAnUnknownIdAnswerTheNotFoundPageOnEveryKindRoute()
    {
        await using var host = await StartAsync();
        await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 1, new DateTimeOffset(2026, 6, 2, 0, 0, 0, TimeSpan.Zero));
        await StatementsSeed.AddAsync(host.OperationalDatabase, Statement);

        foreach (var kind in new[] { RecordPageEndpoints.ExecutiveActionPath, RecordPageEndpoints.DocumentChangePath })
            await GetPageAsync(host, Path(kind, Statement.RecordId), HttpStatusCode.NotFound);
        foreach (var kind in new[] { RecordPageEndpoints.StatementPath, RecordPageEndpoints.ExecutiveActionPath, RecordPageEndpoints.DocumentChangePath })
        {
            await GetPageAsync(host, Path(kind, "bad-id"), HttpStatusCode.NotFound);
            await GetPageAsync(host, Path(kind, new string('f', 64)), HttpStatusCode.NotFound);
        }
    }
}
