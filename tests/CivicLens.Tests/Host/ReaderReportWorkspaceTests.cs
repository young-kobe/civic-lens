using System.Net;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using CivicLens.Application.Reports;
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
        var replacedRow = rows[editor];
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
    public async Task AnOpenReportOpensTheEditorWithTheReaderWordsAboveTheFormAndTheQueueLinksStraightToIt()
    {
        await SeedRelationshipAsync();
        var report = await SaveReportAsync(OnRelationship() with { Text = Script });

        var list = await host.GetDocumentAsync("/Review/Reports");
        var page = await host.GetDocumentAsync(editor);

        Assert.Equal(editor, list.QuerySelector("[aria-labelledby='reports'] tbody a.cell-title")!.GetAttribute("href"));
        var panel = page.QuerySelector($"#reader-report-{report.ReportId:D}")!;
        Assert.Contains("Wrong quote", panel.TextContent, StringComparison.Ordinal);
        Assert.Contains(Script, panel.TextContent, StringComparison.Ordinal);
        Assert.Null(panel.QuerySelector("script"));
        Assert.True(panel.CompareDocumentPosition(page.QuerySelector("#draft-form")!).HasFlag(DocumentPositions.Following));
        Assert.Equal(report.ReportId.ToString(), page.QuerySelector("input[form='draft-form'][name='correctedReportIds']")!.GetAttribute("value"));
        Assert.Equal(report.ReportId.ToString(), page.QuerySelector("input[form='decision-form'][name='correctedReportIds']")!.GetAttribute("value"));
    }

    [Fact]
    public async Task DismissClosesTheReportAndAStaleSecondDismissalChangesNothing()
    {
        await SeedRelationshipAsync();
        var report = await SaveReportAsync(OnRelationship());
        var dismissal = DismissalFields(await host.GetDocumentAsync(editor), report.ReportId);

        using var dismissed = await host.Client.PostAsync(editor, new FormUrlEncodedContent(dismissal));
        using var again = await host.Client.PostAsync(editor, new FormUrlEncodedContent(dismissal));

        Assert.Equal(HttpStatusCode.Redirect, dismissed.StatusCode);
        Assert.Contains("status=reportDismissed", dismissed.Headers.Location!.Query, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        var stale = await new HtmlParser().ParseDocumentAsync(await again.Content.ReadAsStringAsync());
        Assert.Contains("Someone resolved this report", stale.QuerySelector(".notice")!.TextContent, StringComparison.Ordinal);
        var kept = (await store.GetAsync(report.ReportId, default))!.Report;
        Assert.Equal((ReaderReportStatus.Dismissed, 1), (kept.Status, kept.Version));
        var after = await host.GetDocumentAsync(editor + "?status=reportDismissed");
        Assert.Null(after.QuerySelector("[id^='reader-report-']"));
        Assert.Contains("The report is dismissed.", after.QuerySelector(".notice")!.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SaveAndApproveWithATypedFixKeepsTheReportUntilThePublishAndNoReaderTextReachesTheRevision()
    {
        await SeedRelationshipAsync();
        var report = await SaveReportAsync(OnRelationship() with { Text = Script });
        var fields = EditorFields(await host.GetDocumentAsync(editor));
        Set(fields, "Input.Reason", "The typed public reason.");

        using var approved = await host.Client.PostAsync(editor, new FormUrlEncodedContent(fields));

        Assert.Equal(HttpStatusCode.Redirect, approved.StatusCode);
        var closed = (await store.GetAsync(report.ReportId, default))!.Report;
        Assert.Equal((ReaderReportStatus.CorrectionNeeded, "Approved with a correction."), (closed.Status, closed.Note));
        Assert.Equal("0", (await seeder.ReadAsync("SELECT count(*) FROM relationship_revisions r WHERE r::text LIKE '%alert(%'")).Single().Single());
        Assert.Equal("1", (await seeder.ReadAsync("SELECT count(*) FROM relationship_revisions r WHERE r::text LIKE '%The typed public reason.%'")).Single().Single());
        var waiting = await host.GetDocumentAsync(editor);
        Assert.Contains("A correction was approved", waiting.QuerySelector($"#reader-report-{report.ReportId:D}")!.TextContent, StringComparison.Ordinal);
        Assert.Null(waiting.QuerySelector("input[name='correctedReportIds']"));
        Assert.Contains("Corrected", await StatusLabelAsync(), StringComparison.Ordinal);

        await new BriefWorldSeeder(host.ConnectionString).ReplaceActiveReleaseAsync(1, DateTimeOffset.UtcNow,
            new PublishedRevisionBinding(PublishedRecordKind.Relationship, ActionPair().PairId, 2));
        Assert.Null((await host.GetDocumentAsync(editor)).QuerySelector("[id^='reader-report-']"));
    }

    [Fact]
    public async Task AFailedApprovalKeepsTheTypedTextAndLeavesTheReportOpen()
    {
        await SeedRelationshipAsync();
        var report = await SaveReportAsync(OnRelationship());
        var fields = EditorFields(await host.GetDocumentAsync(editor));
        Set(fields, "Input.Reason", "Typed text that must survive.");
        Set(fields, "expectedReviewStateVersion", "99");

        using var refused = await host.Client.PostAsync(editor, new FormUrlEncodedContent(fields));

        Assert.NotEqual(HttpStatusCode.Redirect, refused.StatusCode);
        var kept = await new HtmlParser().ParseDocumentAsync(await refused.Content.ReadAsStringAsync());
        Assert.Equal("Typed text that must survive.", kept.QuerySelector("#Input_Reason")!.GetAttribute("value"));
        Assert.NotNull(kept.QuerySelector("input[name='correctedReportIds']"));
        Assert.Equal(ReaderReportStatus.Open, (await store.GetAsync(report.ReportId, default))!.Report.Status);
        Assert.Equal("1", (await seeder.ReadAsync("SELECT count(*) FROM relationship_revisions")).Single().Single());
    }

    [Fact]
    public async Task ASaveWithoutApprovalLeavesTheReportOpen()
    {
        await SeedRelationshipAsync();
        var report = await SaveReportAsync(OnRelationship());
        var fields = EditorFields(await host.GetDocumentAsync(editor));
        fields.RemoveAll(field => field.Key == "approve");
        Set(fields, "Input.Reason", "Saved only.");

        using var saved = await host.Client.PostAsync(editor, new FormUrlEncodedContent(fields));

        Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);
        Assert.Equal(ReaderReportStatus.Open, (await store.GetAsync(report.ReportId, default))!.Report.Status);
    }

    [Fact]
    public async Task ASeparateApproveOfASavedFixClosesTheReportInTheSameRequestBecauseThePageSendsItsIdWithTheDecision()
    {
        await SeedRelationshipAsync();
        var report = await SaveReportAsync(OnRelationship());
        var fields = EditorFields(await host.GetDocumentAsync(editor));
        fields.RemoveAll(field => field.Key == "approve");
        Set(fields, "Input.Reason", "Saved first.");
        using var saved = await host.Client.PostAsync(editor, new FormUrlEncodedContent(fields));
        Assert.Equal(ReaderReportStatus.Open, (await store.GetAsync(report.ReportId, default))!.Report.Status);
        var page = await host.GetDocumentAsync(editor);
        var decision = Fields(page.QuerySelector("#decision-form")!);
        decision.AddRange(page.QuerySelectorAll("input[form='decision-form']")
            .Select(input => new KeyValuePair<string, string>(input.GetAttribute("name")!, input.GetAttribute("value") ?? "")));
        Set(decision, "kind", "Approve");

        using var approved = await host.Client.PostAsync(editor, new FormUrlEncodedContent(decision));

        Assert.Equal(HttpStatusCode.Redirect, approved.StatusCode);
        Assert.Equal(ReaderReportStatus.CorrectionNeeded, (await store.GetAsync(report.ReportId, default))!.Report.Status);
    }

    [Fact]
    public void EveryEditorShowsTheReportPanelOfItsOwnKindAndOnlyAnEditableOneCanCloseByApproval()
    {
        var editors = new (string File, PublishedRecordKind Kind, string idParameter)[]
        {
            ("Documents/DraftEditor.razor", PublishedRecordKind.DocumentChange, "DraftId"),
            ("Statements/StatementEditor.razor", PublishedRecordKind.Statement, "CandidateId"),
            ("Actions/ActionEditor.razor", PublishedRecordKind.Action, "RecordId"), ("Votes/VoteEditor.razor", PublishedRecordKind.Vote, "RecordId"),
            ("Relationships/RelationshipEditor.razor", PublishedRecordKind.Relationship, "RecordId"),
            ("Briefs/BriefEditor.razor", PublishedRecordKind.Brief, "RecordId"),
            ("ExecutiveActions/ExecutiveActionEditor.razor", PublishedRecordKind.ExecutiveAction, "RecordId")
        };

        foreach (var (file, kind, idParameter) in editors)
        {
            var path = Path.Combine(RepositoryRoot.FullName, "src", "CivicLens.Host", file);
            var markup = File.ReadAllText(path);
            var code = File.ReadAllText(path + ".cs");
            var panel = Regex.Match(markup, @"<ReaderReportPanel [^>]*/>").Value;
            var editable = kind != PublishedRecordKind.ExecutiveAction;
            Assert.Contains($"Kind=\"PublishedRecordKind.{kind}\"", panel, StringComparison.Ordinal);
            Assert.Contains($"RecordId=\"@{idParameter}\"", panel, StringComparison.Ordinal);
            Assert.Equal(editable, panel.Contains("EditFormId=\"draft-form\"", StringComparison.Ordinal));
            Assert.Equal(editable, markup.Contains("id=\"draft-form\"", StringComparison.Ordinal));
            Assert.Equal(editable, code.Contains("RunSaveAsync(", StringComparison.Ordinal));
        }
        Assert.Equal(Enum.GetValues<PublishedRecordKind>().Length, editors.Length);
    }

    [Fact]
    public async Task AnExecutiveActionReportCanBeDismissedButOffersNoApprovalToCloseIt()
    {
        var recordId = await new ExecutiveActionSeeder(host.ConnectionString, host.ArtifactDirectory)
            .AdmitAsync(FederalRegisterFixtures.TrumpOrder, "donald-trump");
        var report = await SaveReportAsync(Report() with { RecordId = recordId, Kind = PublishedRecordKind.ExecutiveAction });

        var shown = await host.GetDocumentAsync($"/Review/ExecutiveActions/{recordId}");

        var panel = shown.QuerySelector($"#reader-report-{report.ReportId:D}")!;
        Assert.Contains("No person edits this record", panel.TextContent, StringComparison.Ordinal);
        Assert.NotNull(panel.QuerySelector("button[form='dismiss-report']"));
        Assert.Null(shown.QuerySelector("input[name='correctedReportIds']"));
    }

    [Fact]
    public async Task TheReportPageDismissesAnOpenReportAndShowsTheResolutionOfAClosedOne()
    {
        var report = await SaveReportAsync(Report());
        var url = $"/Review/Reports/{report.ReportId}";
        var open = await host.GetDocumentAsync(url);

        using var dismissed = await host.Client.PostAsync(url, new FormUrlEncodedContent(DismissalFields(open, report.ReportId)));

        Assert.Equal(HttpStatusCode.Redirect, dismissed.StatusCode);
        var after = await host.GetDocumentAsync(url);
        Assert.Null(after.QuerySelector("#decision"));
        Assert.Contains("Dismissed", after.QuerySelector("[aria-labelledby='resolution']")!.TextContent, StringComparison.Ordinal);
        Assert.Contains("auth0|friend", after.QuerySelector("[aria-labelledby='resolution']")!.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ADismissedReportShowsNoPanelOnTheRecordPage()
    {
        await SeedRelationshipAsync();
        var dismissed = await SaveReportAsync(OnRelationship());
        await store.DismissAsync("auth0|friend", dismissed.ReportId, 0, default);

        var shown = await host.GetDocumentAsync(editor);

        Assert.Null(shown.QuerySelector("[id^='reader-report-']"));
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
    public async Task TheRailLinksToTheReportQueueAndMarksOnlyThatLinkCurrentOnTheQueueAndReportPages()
    {
        var report = await SaveReportAsync(Report());

        foreach (var page in new[] { "/Review/Reports", $"/Review/Reports/{report.ReportId:D}" })
        {
            var shown = await host.GetDocumentAsync(page);

            var links = shown.QuerySelectorAll(".rail .nav-link").Where(link => link.GetAttribute("aria-current") == "page").ToArray();
            var current = Assert.Single(links);
            Assert.Equal("/Review/Reports", current.GetAttribute("href"));
            Assert.Contains("Reader reports", current.TextContent, StringComparison.Ordinal);
        }
    }

    private static List<KeyValuePair<string, string>> DismissalFields(IDocument page, Guid reportId)
    {
        var fields = Fields(page.QuerySelector("form#dismiss-report")!);
        var button = page.QuerySelector($"button[form='dismiss-report'][value^='{reportId:D}:']")!;
        fields.Add(new(button.GetAttribute("name")!, button.GetAttribute("value")!));
        return fields;
    }

    private async Task<string> StatusLabelAsync() =>
        (await host.GetDocumentAsync("/Review/Reports?reports=resolved")).QuerySelector("[aria-labelledby='reports'] tbody")!.TextContent;

    private async Task<PulledReport> SaveReportAsync(PulledReport report)
    {
        await store.SavePulledAsync(Batch(report), default);
        return report;
    }

    private static PulledReport OnRelationship() => Report() with
    {
        RecordId = ActionPair().PairId,
        Kind = PublishedRecordKind.Relationship,
        RevisionNumber = 1
    };

    private static List<KeyValuePair<string, string>> EditorFields(IDocument page)
    {
        var fields = Fields(page.QuerySelector("#draft-form")!);
        fields.AddRange(page.QuerySelectorAll("input[form='draft-form']")
            .Select(input => new KeyValuePair<string, string>(input.GetAttribute("name")!, input.GetAttribute("value") ?? "")));
        fields.Add(new("approve", "true"));
        return fields;
    }

    private async Task SeedRelationshipAsync()
    {
        await seeder.StatementAsync(Statement());
        await seeder.ActionAsync(Action());
        await seeder.RevisionAsync(Revision() with { StatementQuote = Span(BillWords) });
    }
}
