using System.Net;
using CivicLens.Application.Publication;
using CivicLens.Infrastructure.Analysis;
using CivicLens.Infrastructure.Reports;
using CivicLens.Publication.Contracts;
using CivicLens.Tests.Fixtures;
using CivicLens.Tests.Infrastructure.Collection;
using static CivicLens.Tests.Fixtures.RelationshipFixtures;
using static CivicLens.Tests.Host.ReviewWorkspaceHost;
using static CivicLens.Tests.Publication.Contracts.PulledReportTests;

namespace CivicLens.Tests.Host;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class ReaderReportWorkspaceTests(PostgresCollection postgres) : IAsyncLifetime
{
    private const string Script = "<script>alert('x')</script>";

    private readonly ReviewWorkspaceHost host = new(postgres);
    private RelationshipWorldSeeder seeder = null!;
    private PostgresReaderReportStore store = null!;
    private string editor = null!;

    public async Task InitializeAsync()
    {
        await host.StartAsync();
        seeder = new RelationshipWorldSeeder(host.ConnectionString);
        store = new PostgresReaderReportStore(AnalysisDbContextFactory.Create(host.ConnectionString));
        editor = $"/Review/Relationships/{ActionPair().PairId}";
        host.Client.DefaultRequestHeaders.Add("Cookie", host.CreateCookie("auth0|friend"));
    }

    public Task DisposeAsync() => host.StopAsync();

    [Fact]
    public async Task OnlyAConfiguredSubjectMayOpenTheQueueAndAReportThatDoesNotExistIsNotFound()
    {
        host.Client.DefaultRequestHeaders.Remove("Cookie");
        var report = await SaveReportAsync(Report());

        foreach (var page in new[] { "/Review/Reports", $"/Review/Reports/{report.ReportId}" })
        {
            using var anonymous = await host.Client.GetAsync(page);
            Assert.NotEqual(HttpStatusCode.OK, anonymous.StatusCode);
            using var stranger = new HttpRequestMessage(HttpMethod.Get, page);
            stranger.Headers.Add("Cookie", host.CreateCookie("auth0|stranger"));
            using var refused = await host.Client.SendAsync(stranger);
            Assert.NotEqual(HttpStatusCode.OK, refused.StatusCode);
        }
        host.Client.DefaultRequestHeaders.Add("Cookie", host.CreateCookie("auth0|friend"));
        using var missing = await host.Client.GetAsync($"/Review/Reports/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task TheQueueShowsReaderTextAsPlainTextTheReplacedRevisionAndNoEditorLinkForARecordThatIsNotInReview()
    {
        await SeedRelationshipAsync();
        var replaced = await SaveReportAsync(Report() with
        {
            RecordId = ActionPair().PairId,
            Kind = PublishedRecordKind.Relationship,
            Text = Script,
            RevisionNumber = 1
        });
        await seeder.ExecuteAsync("SET session_replication_role = replica; UPDATE relationship_drafts SET current_revision_number = 3");
        var unknown = await SaveReportAsync(Report() with { RecordId = new string('e', 64), Kind = PublishedRecordKind.Vote, RevisionNumber = 2 });

        var list = await host.GetDocumentAsync("/Review/Reports");
        var detail = await host.GetDocumentAsync($"/Review/Reports/{replaced.ReportId}");

        var rows = list.QuerySelectorAll("[aria-labelledby='reports'] tbody tr").ToDictionary(row => row.QuerySelector("a.cell-title")!.GetAttribute("href")!);
        var replacedRow = rows[$"/Review/Reports/{replaced.ReportId}"];
        var unknownRow = rows[$"/Review/Reports/{unknown.ReportId}"];
        Assert.Contains("Revision 1, current is 3", replacedRow.TextContent, StringComparison.Ordinal);
        Assert.NotNull(replacedRow.QuerySelector($"a[href='{editor}']"));
        Assert.Contains("Revision 2", unknownRow.TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain("current is", unknownRow.TextContent, StringComparison.Ordinal);
        Assert.Null(unknownRow.QuerySelector("a[href^='/Review/Votes/']"));
        Assert.Contains("Not in review", unknownRow.TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain(list.QuerySelectorAll("script"), script => script.TextContent.Contains("alert('x')", StringComparison.Ordinal));
        Assert.Contains(Script, replacedRow.TextContent, StringComparison.Ordinal);
        Assert.Equal(Script, detail.QuerySelector("#report-text")!.TextContent);
        Assert.Null(detail.QuerySelector("#report-text script"));
    }

    [Fact]
    public async Task ACorrectionNeedsANoteAndAResolvedReportCannotBeResolvedAgain()
    {
        var report = await SaveReportAsync(Report());
        var url = $"/Review/Reports/{report.ReportId}";
        var fields = Fields((await host.GetDocumentAsync(url)).QuerySelector("#decision-form")!);

        var noNote = new List<KeyValuePair<string, string>>(fields) { new("kind", "CorrectionNeeded") };
        using var refused = await host.Client.PostAsync(url, new FormUrlEncodedContent(noNote));
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);

        var dismissal = new List<KeyValuePair<string, string>>(fields) { new("kind", "Dismissed") };
        using var dismissed = await host.Client.PostAsync(url, new FormUrlEncodedContent(dismissal));
        Assert.Equal(HttpStatusCode.Redirect, dismissed.StatusCode);
        using var again = await host.Client.PostAsync(url, new FormUrlEncodedContent(dismissal));
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);

        var after = await host.GetDocumentAsync(url);
        Assert.Null(after.QuerySelector("#decision-form"));
        Assert.Contains("Dismissed", after.QuerySelector("[aria-labelledby='resolution']")!.TextContent, StringComparison.Ordinal);
        Assert.Contains("auth0|friend", after.QuerySelector("[aria-labelledby='resolution']")!.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheRecordPageShowsTheCorrectionBannerUntilANewerRevisionIsPublishedAndAnOpenOrDismissedReportShowsNone()
    {
        await SeedRelationshipAsync();
        var needsFix = await SaveReportAsync(Report() with
        {
            RecordId = ActionPair().PairId,
            Kind = PublishedRecordKind.Relationship,
            Category = ReaderReportProtocol.WrongQuoteCategory,
            Text = Script
        });
        var openOnly = await SaveReportAsync(Report() with { RecordId = ActionPair().PairId, Kind = PublishedRecordKind.Relationship });
        await store.ResolveAsync("auth0|friend", needsFix.ReportId, 0, CivicLens.Application.Reports.ReaderReportStatus.CorrectionNeeded, "Use the full quote.", default);

        var shown = await host.GetDocumentAsync(editor);
        var banner = shown.QuerySelector($"#reader-report-{needsFix.ReportId:D}")!;
        Assert.Contains("Wrong quote", banner.TextContent, StringComparison.Ordinal);
        Assert.Contains("Use the full quote.", banner.TextContent, StringComparison.Ordinal);
        Assert.Contains(Script, banner.TextContent, StringComparison.Ordinal);
        Assert.Equal($"/Review/Reports/{needsFix.ReportId:D}", banner.QuerySelector("a")!.GetAttribute("href"));
        Assert.Null(shown.QuerySelector($"#reader-report-{openOnly.ReportId:D}"));

        await new BriefWorldSeeder(host.ConnectionString).ReplaceActiveReleaseAsync(1, DateTimeOffset.UtcNow,
            new PublishedRevisionBinding(PublishedRecordKind.Relationship, ActionPair().PairId, 2));
        var gone = await host.GetDocumentAsync(editor);
        Assert.Null(gone.QuerySelector("[id^='reader-report-']"));
    }

    [Fact]
    public async Task TheReviewHomeCountsTheOpenReports()
    {
        await SaveReportAsync(Report());
        await SaveReportAsync(Report());

        var home = await host.GetDocumentAsync("/Review");

        var stat = home.QuerySelectorAll(".stat").Single(item => item.TextContent.Contains("Reader reports open", StringComparison.Ordinal));
        Assert.Equal("2", stat.QuerySelector(".stat-value")!.TextContent);
        Assert.Equal("/Review/Reports", stat.GetAttribute("href"));
    }

    [Fact]
    public async Task TheRailLinksToTheReportQueueAndMarksOnlyThatLinkCurrentOnTheQueuePage()
    {
        var queue = await host.GetDocumentAsync("/Review/Reports");

        var links = queue.QuerySelectorAll(".rail .nav-link").Where(link => link.GetAttribute("aria-current") == "page").ToArray();
        var current = Assert.Single(links);
        Assert.Equal("/Review/Reports", current.GetAttribute("href"));
        Assert.Contains("Reader reports", current.TextContent, StringComparison.Ordinal);
    }

    private async Task<PulledReport> SaveReportAsync(PulledReport report)
    {
        await store.SavePulledAsync(Batch(report), default);
        return report;
    }

    private async Task SeedRelationshipAsync()
    {
        await seeder.StatementAsync(Statement());
        await seeder.ActionAsync(Action());
        await seeder.RevisionAsync(Revision() with { StatementQuote = Span(BillWords) });
    }
}
