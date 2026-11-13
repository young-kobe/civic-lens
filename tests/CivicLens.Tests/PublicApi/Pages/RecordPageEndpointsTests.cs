using System.Net;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using CivicLens.PublicApi;
using CivicLens.PublicApi.Pages;
using CivicLens.PublicWeb.Publication;
using CivicLens.Publication.Contracts;
using CivicLens.Tests.Fixtures;
using CivicLens.Tests.Infrastructure.Collection;
using CivicLens.Tests.PublicApi.Briefs;
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

    private static string Path(string id) => $"{ServerPageEndpoints.Prefix}/{PublicationProtocol.RecordPagePath(id)}";

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

        var page = await GetPageAsync(host, Path(Statement.RecordId));

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

        var page = await GetPageAsync(host, Path(ActionId));

        Assert.Equal("Title two", page.QuerySelector("h1")!.TextContent);
        Assert.Contains("Signed by President Jane Roe on 20 Jan 2026. Printed in the Federal Register on 23 Jan 2026, citation 91 FR 1.", page.QuerySelector(".detail-lede")!.TextContent);
        Assert.Equal(["https://fr.example/9", "https://fr.example/5"],
            page.QuerySelectorAll(".detail-facts dd a").Where(link => link.TextContent == "Open the correction").Select(link => link.GetAttribute("href")!));
        Assert.Equal("Executive actions", page.QuerySelectorAll(".public-breadcrumbs a")[1].TextContent);
    }

    [Theory]
    [InlineData("ExecutiveOrder", "What an executive order is", "An executive order is a signed direction from the President to federal agencies.", "Read it to see which agencies it directs and what they must do.")]
    [InlineData("Proclamation", "What a proclamation is", "A proclamation is a formal statement from the President.", "Read the text to see what it announces or orders.")]
    [InlineData("Memorandum", "What a memorandum is", "A memorandum is a written direction from the President.", "Read it to see who it directs and what they must do.")]
    [InlineData("Notice", "What a presidential notice is", "A presidential notice is a document from the President that gives public information", "Read it to see what it announces.")]
    public async Task TheExecutiveActionPageExplainsItsKindWithTheGlossaryMeaningAndEachKindShowsItsOwnReadHint(string kind, string heading, string sentence, string hint)
    {
        await using var host = await StartAsync();
        await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 1, new DateTimeOffset(2026, 6, 2, 0, 0, 0, TimeSpan.Zero));
        await ViewsSeed.ExecuteAsync(host.OperationalDatabase, "SET session_replication_role = replica;\n" + PublicRecordPageRows.ExecutiveAction(ActionId)
            + $"UPDATE executive_action_revisions SET kind = '{kind}';");

        var page = await GetPageAsync(host, Path(ActionId));

        var section = page.QuerySelector("section.executive-explain")!;
        Assert.Equal(["official-fixed", "executive-explain"], section.ClassList.ToArray());
        Assert.Equal(heading, section.QuerySelector("h2")!.TextContent);
        var lead = (IElement)section.QuerySelector("p")!.Clone(true);
        lead.QuerySelector(".term-popover")!.Remove();
        Assert.StartsWith(sentence, lead.TextContent);
        Assert.Equal(hint, section.QuerySelector("p.small")!.TextContent);
        Assert.True(section.NextElementSibling!.ClassList.Contains("ai-summary"));
    }

    [Fact]
    public async Task TheExecutiveActionPageMapsTypedTargetsInPositionOrderAndLinksOnlyActiveReleaseMatches()
    {
        await using var host = await StartAsync();
        await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 2, new DateTimeOffset(2026, 6, 3, 0, 0, 0, TimeSpan.Zero));
        await ViewsSeed.ExecuteAsync(host.OperationalDatabase, "SET session_replication_role = replica;\n" + ExecutiveActionTargetRows.Seed());

        var page = await GetPageAsync(host, Path(ExecutiveActionTargetRows.Source));

        var targets = page.QuerySelectorAll("#executive-links-title + ul > li");
        Assert.Equal(5, targets.Length);
        Assert.Contains("Amends: Executive order 14100", targets[0].TextContent);
        Assert.Equal("EO 14100, January 3, 2025", targets[0].QuerySelector(".detail-target-reference")!.TextContent);
        Assert.Equal(BaseUrl.Join("records/" + ExecutiveActionTargetRows.LinkedOrder + ".html"), targets[0].QuerySelector("a")!.GetAttribute("href"));
        Assert.Contains("Amends: Executive order 14101", targets[1].TextContent);
        Assert.Null(targets[1].QuerySelector("a"));
        Assert.Contains("Revokes: Proclamation 10001", targets[3].TextContent);
        Assert.Equal(BaseUrl.Join("records/" + ExecutiveActionTargetRows.LinkedProclamation + ".html"), targets[3].QuerySelector("a")!.GetAttribute("href"));
    }

    [Fact]
    public async Task TheDocumentChangeRouteRendersTheRedlineFromTheExactStoredOffsets()
    {
        await using var host = await StartAsync();
        var created = await DocumentChangeEndpointsTests.CreateDraftAsync(host, "page", "Book a tour seven days before.", "Book a tour three days before.");
        var saved = await DocumentChangeEndpointsTests.SaveAsync(host, created, "Headline", created.Citations);
        await DocumentChangeEndpointsTests.ApproveAsync(host, saved);
        await DocumentChangeEndpointsTests.CommitAsync(host, 1, null, saved);

        var page = await GetPageAsync(host, Path(saved.DraftId));

        Assert.Equal("Headline", page.QuerySelector("h1")!.TextContent);
        Assert.Equal("seven", page.QuerySelector(".redline del")!.TextContent.Replace("Removed: ", ""));
        Assert.Equal("three", page.QuerySelector(".redline ins")!.TextContent.Replace("Added: ", ""));
        Assert.Equal(["Book a tour seven days before.", "Book a tour three days before."], page.QuerySelectorAll("#source .doc-text").Select(text => text.TextContent));
    }

    [Fact]
    public async Task ADocumentChangeWithBackwardsWordEditsFailsClosedInsteadOfRenderingMisleadingSourceText()
    {
        await using var host = await StartAsync();
        var created = await DocumentChangeEndpointsTests.CreateDraftAsync(host, "backwards-edits", "Old same red policy.", "New same blue policy.");
        var saved = await DocumentChangeEndpointsTests.SaveAsync(host, created, "Changed policy", created.Citations);
        await DocumentChangeEndpointsTests.ApproveAsync(host, saved);
        await DocumentChangeEndpointsTests.CommitAsync(host, 1, null, saved);
        await using var connection = new Npgsql.NpgsqlConnection(host.OperationalDatabase);
        await connection.OpenAsync();
        await using var command = new Npgsql.NpgsqlCommand(
            "UPDATE document_comparison_word_edits SET after_start = 0 WHERE comparison_id = @id AND position = 1", connection);
        command.Parameters.AddWithValue("id", saved.ComparisonId);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());

        using var response = await host.Client.GetAsync(Path(saved.DraftId));

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    [Fact]
    public async Task OneRecordRouteReadsTheKindOnceAndServesEveryKindItHasAPageFor()
    {
        await using var host = await StartAsync();
        await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 1, new DateTimeOffset(2026, 6, 2, 0, 0, 0, TimeSpan.Zero));
        await StatementsSeed.AddAsync(host.OperationalDatabase, Statement);
        await StatementEndpointsTests.AddDetailEvidenceAsync(host.OperationalDatabase, Statement.RecordId, ExtractionId, Source,
            Source.IndexOf("exact quote", StringComparison.Ordinal), "exact quote".Length, Source.IndexOf("Friday", StringComparison.Ordinal), 6);
        await OfficialsSeed.AddOfficialsAsync(host.OperationalDatabase, new SeededOfficial("official-a", "Ann Able"), new SeededOfficial("official-z", "Zed Zane"));
        await ViewsSeed.ExecuteAsync(host.OperationalDatabase, "SET session_replication_role = replica;\n" + PublicRecordPageRows.ExecutiveAction(ActionId));

        Assert.Equal("Headline a", (await GetPageAsync(host, Path(Statement.RecordId))).QuerySelector("h1")!.TextContent);
        Assert.Equal("Title two", (await GetPageAsync(host, Path(ActionId))).QuerySelector("h1")!.TextContent);
    }

    [Fact]
    public async Task ACodeApprovedStatementNamesTheCodeCheckOfItsDateAndAPersonApprovedOneDoesNot()
    {
        var person = new SeededStatement('b', "2026-03-02", ["official-z", "official-a"], ["health", "unlisted"], "Analysis", "Person");

        Assert.Contains(PublishedRecordLabels.ModelDateCheckedByCode, await ReadSourceFoldAsync(Statement));
        Assert.DoesNotContain(PublishedRecordLabels.ModelDateCheckedByCode, await ReadSourceFoldAsync(person));
    }

    private async Task<string> ReadSourceFoldAsync(SeededStatement seeded)
    {
        await using var host = await StartAsync();
        await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 1, new DateTimeOffset(2026, 6, 2, 0, 0, 0, TimeSpan.Zero));
        await StatementsSeed.AddAsync(host.OperationalDatabase, seeded);
        await StatementEndpointsTests.AddDetailEvidenceAsync(host.OperationalDatabase, seeded.RecordId, ExtractionId, Source,
            Source.IndexOf("exact quote", StringComparison.Ordinal), "exact quote".Length, Source.IndexOf("Friday", StringComparison.Ordinal), 6);
        await OfficialsSeed.AddOfficialsAsync(host.OperationalDatabase, new SeededOfficial("official-a", "Ann Able"), new SeededOfficial("official-z", "Zed Zane"));
        return (await GetPageAsync(host, Path(seeded.RecordId))).QuerySelector("#source")!.TextContent;
    }

    [Fact]
    public async Task ARecordOfAKindWithoutAPageAMalformedIdAndAnUnknownIdAnswerTheNotFoundPage()
    {
        await using var host = await StartAsync();
        await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 1, new DateTimeOffset(2026, 6, 2, 0, 0, 0, TimeSpan.Zero));
        await StatementsSeed.AddAsync(host.OperationalDatabase, Statement);
        await ViewsSeed.ExecuteAsync(host.OperationalDatabase, $"UPDATE publication_record_states SET kind = 'brief' WHERE record_id = '{Statement.RecordId}';");

        await GetPageAsync(host, Path(Statement.RecordId), HttpStatusCode.NotFound);
        await GetPageAsync(host, $"{ServerPageEndpoints.Prefix}/records/bad-id.html", HttpStatusCode.NotFound);
        await GetPageAsync(host, Path(new string('f', 64)), HttpStatusCode.NotFound);
        foreach (var kindRoute in new[] { "statements", "executive-actions", "document-changes" })
            await GetPageAsync(host, $"{ServerPageEndpoints.Prefix}/{kindRoute}/{Statement.RecordId}", HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task TheBriefIsOneMoreKindOfTheOneRouteAndRendersItsParagraphsAndPinnedCitationsInOrder()
    {
        await using var host = await StartAsync();
        await PublishedBriefContentSeed.SeedAsync(host.OperationalDatabase);

        var page = await GetPageAsync(host, Path(BriefFixtures.RecordId));

        Assert.Equal("Published brief headline", page.QuerySelector("h1")!.TextContent);
        Assert.Equal(["Textparagraphone[R1]", "Textparagraphtwo[R2][R3]"], page.QuerySelectorAll(".ai-summary p.record-body").Select(text => Regex.Replace(text.TextContent, @"\s+", "")));
        Assert.Equal(["Pinned statement headline", "Pinned document headline", "Bill 7"], page.QuerySelectorAll(".detail-cited li a").Select(link => link.TextContent));
        Assert.Null(page.QuerySelector(".detail-lede"));
        Assert.NotNull(page.QuerySelector(".ai-summary-label"));
    }

    public static TheoryData<PublishedRecordKind> WithdrawableKinds => new()
    {
        PublishedRecordKind.Vote,
        PublishedRecordKind.Statement,
        PublishedRecordKind.ExecutiveAction,
        PublishedRecordKind.DocumentChange,
        PublishedRecordKind.Brief
    };

    [Theory]
    [MemberData(nameof(WithdrawableKinds))]
    public async Task AWithdrawnRecordAnswersTheNoticeOnTheOneRouteWithTheExactRevisionDatesNoteAndCorrectionHistory(PublishedRecordKind kind)
    {
        await using var host = await StartAsync();
        await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 6, new DateTimeOffset(2026, 6, 6, 0, 0, 0, TimeSpan.Zero));
        var id = new string('c', kind == PublishedRecordKind.DocumentChange ? 32 : 64);
        var first = new DateTimeOffset(2026, 1, 2, 3, 4, 0, TimeSpan.Zero).UtcTicks;
        var withdrawn = new DateTimeOffset(2026, 6, 2, 12, 30, 0, TimeSpan.Zero).UtcTicks;
        var edit = new DateTimeOffset(2026, 3, 3, 0, 0, 0, TimeSpan.Zero).UtcTicks;
        var text = PublicationProtocol.KindText(kind);
        await ViewsSeed.ExecuteAsync(host.OperationalDatabase, $"""
            SET session_replication_role = replica;
            INSERT INTO published_records (kind, record_id, first_published_at_utc_ticks) VALUES ('{text}', '{id}', {first});
            INSERT INTO publication_withdrawals (kind, record_id, withdrawn_revision_number, withdrawn_at_utc_ticks, note, first_release, last_release) VALUES
                ('{text}', '{id}', 5, {withdrawn}, 'Withdrawn after a report', 6, NULL);
            INSERT INTO publication_withdrawal_corrections (kind, record_id, first_release, "position", replaced_revision_number, published_at_utc_ticks, note, facts_unchanged) VALUES
                ('{text}', '{id}', 6, 0, 4, {edit}, 'A person fixed the date', false),
                ('{text}', '{id}', 6, 1, 2, {edit}, NULL, false);
            """);

        var page = await GetPageAsync(host, Path(id));

        Assert.Equal("This record was withdrawn", page.QuerySelector("h1")!.TextContent);
        Assert.Equal(["Home", PublishedRecordLabels.KindPlural(kind)], page.QuerySelectorAll(".public-breadcrumbs a").Select(link => link.TextContent));
        Assert.Equal([PublishedRecordLabels.Kind(kind), id, "5", "2 Jan 2026, 03:04 UTC", "2 Jun 2026, 12:30 UTC", "Withdrawn after a report"],
            page.QuerySelectorAll("dd").Select(value => value.TextContent.Trim()));
        var history = page.QuerySelectorAll(".corrections li").Select(item => item.TextContent).ToArray();
        Assert.Equal(2, history.Length);
        Assert.Contains("Replaces revision 4.", history[0]);
        Assert.Contains("A person fixed the date", history[0]);
        Assert.Contains("Replaces revision 2.", history[1]);
        Assert.StartsWith(BaseUrl.Join(string.Empty), page.QuerySelector(".public-breadcrumbs a")!.GetAttribute("href"));
    }

    [Fact]
    public async Task AnIdWithdrawnUnderTwoKindsAnswersTheNoticeOfTheFirstKindInTextOrderWithOnlyItsCorrections()
    {
        await using var host = await StartAsync();
        await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 6, new DateTimeOffset(2026, 6, 6, 0, 0, 0, TimeSpan.Zero));
        var id = new string('d', 64);
        var when = new DateTimeOffset(2026, 3, 3, 0, 0, 0, TimeSpan.Zero).UtcTicks;
        await ViewsSeed.ExecuteAsync(host.OperationalDatabase, $"""
            SET session_replication_role = replica;
            INSERT INTO published_records (kind, record_id, first_published_at_utc_ticks) VALUES ('statement', '{id}', {when}), ('action', '{id}', {when});
            INSERT INTO publication_withdrawals (kind, record_id, withdrawn_revision_number, withdrawn_at_utc_ticks, note, first_release, last_release) VALUES
                ('statement', '{id}', 3, {when}, 'Statement notice', 6, NULL),
                ('action', '{id}', 2, {when}, 'Action notice', 6, NULL);
            INSERT INTO publication_withdrawal_corrections (kind, record_id, first_release, "position", replaced_revision_number, published_at_utc_ticks, note, facts_unchanged) VALUES
                ('statement', '{id}', 6, 0, 1, {when}, 'Statement fix', false),
                ('action', '{id}', 6, 0, 1, {when}, 'Action fix', false);
            """);

        var page = await GetPageAsync(host, Path(id));

        Assert.Contains("Action notice", page.QuerySelector("main")!.TextContent);
        Assert.DoesNotContain("Statement", page.QuerySelector("main")!.TextContent.Replace(PublishedRecordLabels.Kind(PublishedRecordKind.Statement), string.Empty));
        Assert.Single(page.QuerySelectorAll(".corrections li"));
        Assert.Contains("Action fix", page.QuerySelector(".corrections li")!.TextContent);
    }
}
