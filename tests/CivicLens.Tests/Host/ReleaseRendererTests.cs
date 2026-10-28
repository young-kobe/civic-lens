using CivicLens.Application.Issues;
using System.Text.RegularExpressions;
using AngleSharp.Html.Parser;
using CivicLens.Application.Publication;
using CivicLens.Host.Publication;
using CivicLens.Publication.Contracts;
using CivicLens.Tests.Fixtures;

namespace CivicLens.Tests.Host;

public sealed class ReleaseRendererTests
{
    private const string BeforeText = "Old <b>rule</b> applies. Keep this.";
    private const string AfterText = "New rule & scope applies. Keep this.";
    private static readonly string BeforeId = new('a', 64);
    private static readonly string AfterId = new('b', 64);

    [Fact]
    public async Task RecordPageEncodesUntrustedTextAndMarksExactlyTheCitedRanges()
    {
        var html = await new ReleaseRenderer(null, null).RenderRecordAsync(Record(), PublicationCatalogs.None, CancellationToken.None);

        Assert.DoesNotContain("<b>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<script>", html, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;", html, StringComparison.Ordinal);
        Assert.Equal("&lt;b&gt;rule&lt;/b&gt;", MarkedText(html, "cite-1"));
        Assert.Equal("rule &amp; scope", MarkedText(html, "cite-2"));
        Assert.Contains("href=\"#cite-1\"", html, StringComparison.Ordinal);
        Assert.Contains("href=\"#cite-2\"", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("cite-1")]
    [InlineData("cite-2")]
    [InlineData("cite-change-date")]
    public async Task CitedTextIsNeverInsideAClosedDetailsElementSoLinksWorkWithoutScript(string anchor)
    {
        var html = await new ReleaseRenderer(null, null).RenderRecordAsync(Record(), PublicationCatalogs.None, CancellationToken.None);
        var before = html[..html.IndexOf($"id=\"{anchor}\"", StringComparison.Ordinal)];
        var open = new Stack<string>();
        foreach (Match tag in Regex.Matches(before, "<details[^>]*>|</details>"))
        {
            if (tag.Value == "</details>") open.Pop();
            else open.Push(tag.Value);
        }

        Assert.All(open, tag => Assert.Matches(new Regex(@"\sopen[\s>=]"), tag));
    }

    [Fact]
    public async Task TheComparisonStartsInRedlineWithAHiddenSwitchWhoseFirstChoiceIsPressed()
    {
        var html = await new ReleaseRenderer(null, null).RenderRecordAsync(Record(), PublicationCatalogs.None, CancellationToken.None);

        var document = new HtmlParser().ParseDocument(html);
        Assert.NotNull(document.QuerySelector(".comparison[data-layout='redline'] [data-layout-switch][hidden]"));
        Assert.Equal(["redline:true", "side:false"], document.QuerySelectorAll(".comparison [data-layout-choice]")
            .Select(button => $"{button.GetAttribute("data-layout-choice")}:{button.GetAttribute("aria-pressed")}"));
    }

    [Fact]
    public async Task FullVersionsKeepALeadingNewlineBecauseThePageMustMatchTheApprovedEvidence()
    {
        var record = Record("\n" + BeforeText, "\n" + AfterText);

        var html = await new ReleaseRenderer(null, null).RenderRecordAsync(record, PublicationCatalogs.None, CancellationToken.None);

        var shown = new HtmlParser().ParseDocument(html).QuerySelectorAll(".doc-text").Select(text => text.TextContent);
        Assert.Equal([record.Before.Text, record.After.Text], shown);
    }

    [Fact]
    public async Task RecordPageIsPublicStaticAndRelative()
    {
        var html = await new ReleaseRenderer(null, null).RenderRecordAsync(Record(), PublicationCatalogs.None, CancellationToken.None);

        Assert.StartsWith("<!DOCTYPE html>", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<html lang=\"en\">", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Private", html, StringComparison.Ordinal);
        Assert.DoesNotContain("not published", html, StringComparison.Ordinal);
        Assert.Contains("href=\"../assets/css/theme.css\"", html, StringComparison.Ordinal);
        Assert.Contains("src=\"../assets/js/app.js\"", html, StringComparison.Ordinal);
        Assert.Contains("href=\"../all.html\"", html, StringComparison.Ordinal);
        Assert.DoesNotMatch(new Regex("(?:href|src)=\"(?:/|//)"), html);
        Assert.Contains("rel=\"noopener noreferrer nofollow\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RecordPageKeepsObservationDatesApartFromTheChangeDate()
    {
        var withDate = await new ReleaseRenderer(null, null).RenderRecordAsync(Record(), PublicationCatalogs.None, CancellationToken.None);
        var withoutDate = await new ReleaseRenderer(null, null).RenderRecordAsync(
            Record() with { ChangeDate = null, ChangeDateEvidence = null }, PublicationCatalogs.None, CancellationToken.None);

        Assert.Contains("Actual change date", withDate, StringComparison.Ordinal);
        Assert.Contains("2026-03-01", withDate, StringComparison.Ordinal);
        Assert.Contains("cite-change-date", withDate, StringComparison.Ordinal);
        Assert.DoesNotContain("Actual change date", withoutDate, StringComparison.Ordinal);
        foreach (var html in new[] { withDate, withoutDate })
        {
            Assert.Contains("Earlier version 2026-04-02 09:30 UTC", html, StringComparison.Ordinal);
            Assert.Contains("later version 2026-05-03 10:45 UTC", html, StringComparison.Ordinal);
            Assert.Contains("Approved 2026-05-10", html, StringComparison.Ordinal);
            Assert.Contains("first published 2026-05-11", html, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task RecordPageShowsChangedPassagesSlicedFromThePublishedTexts()
    {
        var html = await new ReleaseRenderer(null, null).RenderRecordAsync(Record(), PublicationCatalogs.None, CancellationToken.None);

        Assert.Contains("Changed passage 1", html, StringComparison.Ordinal);
        Assert.Contains("hidden", html, StringComparison.Ordinal);
        Assert.DoesNotContain("/Documents/Citation", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SearchPageCallsTheServiceAtTheConfiguredAddressAndStaysHiddenUntilScriptRuns()
    {
        var html = await new ReleaseRenderer("https://api.example.test", null).RenderSearchAsync(ReleaseWithFilters(), SearchFacets(), CancellationToken.None);
        var document = new HtmlParser().ParseDocument(html);

        var form = document.QuerySelector("form[data-search-api]")!;
        Assert.Equal("https://api.example.test", form.GetAttribute("data-search-api"));
        Assert.True(form.Closest("section")!.HasAttribute("hidden"));
        Assert.Equal(["q", "kind", "official", "issue", "policyArea", "source", "from", "to"],
            form.QuerySelectorAll("input, select").Select(control => control.GetAttribute("name")));
        Assert.Equal(["", "documentChange", "statement", "action", "brief", "vote", "relationship"],
            document.QuerySelectorAll("#search-kind option").Select(option => option.GetAttribute("value")));
    }

    [Fact]
    public async Task SearchPageFiltersListTheOfficialsAndSourcesOfTheManifestAndTheIssuesAndAreasWithTheirCatalogNames()
    {
        var html = await new ReleaseRenderer("https://api.example.test", null).RenderSearchAsync(ReleaseWithFilters(), SearchFacets(), CancellationToken.None);
        var document = new HtmlParser().ParseDocument(html);

        string[] Options(string id) => [.. document.QuerySelectorAll($"#{id} option").Select(option => option.GetAttribute("value")!)];
        Assert.Equal(["", "o1", "o2"], Options("search-official"));
        Assert.Equal(["Ann Roe", "Jane Doe"], document.QuerySelectorAll("#search-official option:not([value=''])").Select(option => option.TextContent));
        Assert.Equal(["", "housing", "transit"], Options("search-issue"));
        Assert.Equal(["Any", "Housing costs", "Transit"], document.QuerySelectorAll("#search-issue option").Select(option => option.TextContent));
        Assert.Equal(["", "area-one"], Options("search-policy-area"));
        Assert.Equal(["Any", "Area One"], document.QuerySelectorAll("#search-policy-area option").Select(option => option.TextContent));
        Assert.Equal(["", "a.gov", "b.gov"], Options("search-source"));
    }

    [Fact]
    public async Task SearchPageStatesTheSourceTextLimitSoReadersKnowWhatSearchReads()
    {
        var html = await new ReleaseRenderer("https://api.example.test", null).RenderSearchAsync(ReleaseWithFilters(), SearchFacets(), CancellationToken.None);

        Assert.Contains("first 200,000 characters of each source text", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SearchPageWithoutAServiceAddressSaysThatSearchIsNotAvailableAndShowsNoForm()
    {
        var html = await new ReleaseRenderer(null, null).RenderSearchAsync(ReleaseWithFilters(), SearchFacets(), CancellationToken.None);
        var document = new HtmlParser().ParseDocument(html);

        Assert.Contains("Search is not available", html, StringComparison.Ordinal);
        Assert.Null(document.QuerySelector("form"));
    }

    [Fact]
    public async Task EveryPublicPageLinksToTheSearchPageAndTheAllRecordsPageHoldsNoBrowserSearch()
    {
        var index = await new ReleaseRenderer(null, null).RenderAllRecordsAsync(ReleaseOf(1), 1, CancellationToken.None);
        var record = await new ReleaseRenderer(null, null).RenderRecordAsync(Record(), PublicationCatalogs.None, CancellationToken.None);

        Assert.Contains("href=\"search.html\"", index, StringComparison.Ordinal);
        Assert.Contains("href=\"../search.html\"", record, StringComparison.Ordinal);
        Assert.DoesNotContain("data-release-search", index, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EveryRecordPageLinksToTheReportPageWithItsRecordAndRevisionSoAReportBindsToTheVersionTheReaderSaw()
    {
        var renderer = new ReleaseRenderer(null, null);
        var record = Record();
        var vote = Vote();

        var pages = new[]
        {
            (record.RecordId, record.RevisionNumber, await renderer.RenderRecordAsync(record, PublicationCatalogs.None, CancellationToken.None)),
            (vote.RecordId, vote.RevisionNumber, await renderer.RenderVoteAsync(vote, null, [], PublicationCatalogs.None, CancellationToken.None))
        };

        foreach (var (recordId, revision, html) in pages)
            Assert.Equal($"../report.html?recordId={recordId}&revision={revision}",
                new HtmlParser().ParseDocument(html).QuerySelector("a[href^='../report.html']")!.GetAttribute("href"));
    }

    [Fact]
    public async Task TheMethodPageNamesTheSubstantiveVoteKindsAndTheThreeIssueCountsFromTheirOwners()
    {
        var html = await new ReleaseRenderer(null, null).RenderMethodAsync(CancellationToken.None);

        var text = new HtmlParser().ParseDocument(html).QuerySelector("main")!.TextContent;
        Assert.Contains("Amendment, Suspension of the rules, Passage, Conference report, Veto override, Nomination.", text, StringComparison.Ordinal);
        Assert.Contains("A vote is substantive when its kind is one of these:", text, StringComparison.Ordinal);
        Assert.Contains("Votes they cast: the substantive roll call votes on the issue", text, StringComparison.Ordinal);
        Assert.Contains("Their statements: ", text, StringComparison.Ordinal);
        Assert.Contains("Bills they sponsored: ", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Too few records", text, StringComparison.Ordinal);
        Assert.Contains("A missed vote means the official did not vote on it.", text, StringComparison.Ordinal);
        Assert.Contains("A roll call that was not published is in neither number.", text, StringComparison.Ordinal);
        Assert.Contains("A split vote is a substantive roll call vote where most Democrats and most Republicans voted on opposite sides.", text, StringComparison.Ordinal);
        Assert.Contains($"on at least {TalkAndActionProfile.MinimumRecordsForShare} split votes", text, StringComparison.Ordinal);
        Assert.Contains("policy area of the bill in Congress.gov", text, StringComparison.Ordinal);
        Assert.Contains("Some tags can still be wrong", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheMethodPageExplainsALinkWithTheFourPublicLinkWordsAndSaysThatALinkIsNotAVerdict()
    {
        var html = await new ReleaseRenderer(null, null).RenderMethodAsync(CancellationToken.None);

        var text = new HtmlParser().ParseDocument(html).QuerySelector("main")!.TextContent;
        foreach (var type in Enum.GetValues<PublishedRelationshipType>())
            Assert.Contains($"\"{PublishedRecordLabels.RelationshipType(type)}\" means", text, StringComparison.Ordinal);
        Assert.Contains("A link is not a verdict.", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReportPageHoldsEveryProtocolCategoryWithALabelAndPostsToTheConfiguredServiceWithTheSiteKey()
    {
        var html = await new ReleaseRenderer("https://api.example.test", "site-key-1").RenderReportAsync(CancellationToken.None);
        var document = new HtmlParser().ParseDocument(html);

        var form = document.QuerySelector("form[data-report-api]")!;
        Assert.Equal("https://api.example.test" + ReaderReportProtocol.SubmitPath, form.GetAttribute("data-report-api"));
        Assert.Equal("site-key-1", document.QuerySelector(".cf-turnstile")!.GetAttribute("data-sitekey"));
        Assert.Equal(["", .. ReaderReportProtocol.Categories],
            document.QuerySelectorAll("#report-category option").Select(option => option.GetAttribute("value")));
        Assert.All(document.QuerySelectorAll("#report-category option[value]:not([value=''])"),
            option => Assert.Equal(PublishedRecordLabels.ReportCategory(option.GetAttribute("value")!), option.TextContent));
        Assert.Equal([ReaderReportProtocol.RecordIdField, ReaderReportProtocol.RevisionField],
            form.QuerySelectorAll("input[type=hidden][data-report-query]").Select(input => input.GetAttribute("name")));
        Assert.Equal(ReaderReportProtocol.TextField, document.QuerySelector("#report-text")!.GetAttribute("name"));
        Assert.Equal(ReaderReportProtocol.MaximumTextCharacters.ToString(), document.QuerySelector("#report-text")!.GetAttribute("maxlength"));
        Assert.True(form.Closest("section")!.HasAttribute("hidden"));
        var result = document.QuerySelector("[data-report-result]")!;
        Assert.Equal("status", result.GetAttribute("role"));
        Assert.Equal("-1", result.GetAttribute("tabindex"));
    }

    [Fact]
    public async Task ReportPageAnnouncesAFailureThroughItsOwnAlertRegionSoNoReaderMissesAFailedSend()
    {
        var html = await new ReleaseRenderer("https://api.example.test", "site-key-1").RenderReportAsync(CancellationToken.None);
        var document = new HtmlParser().ParseDocument(html);

        var failure = document.QuerySelector("[data-report-failure]")!;
        Assert.Equal("alert", failure.GetAttribute("role"));
        Assert.Equal("-1", failure.GetAttribute("tabindex"));
    }

    [Fact]
    public async Task MainTakesFocusFromTheSkipLinkOnEveryPageType()
    {
        foreach (var (name, _, html, _) in await RenderEveryPageTypeAsync())
        {
            var document = new HtmlParser().ParseDocument(html);

            Assert.Equal("#main", document.QuerySelector("a.skip")!.GetAttribute("href"));
            var main = document.QuerySelector("main#main")!;
            Assert.True(main.GetAttribute("tabindex") == "-1", name);
        }
    }

    [Fact]
    public async Task EveryLinkBackToTheSourceTextOrTheRelationNamesItsTargetSoNoTwoLinksOnAPageSoundTheSame()
    {
        var renderer = new ReleaseRenderer(null, null);
        var statement = Statement();
        var action = Action();
        var vote = Vote() with { RecordId = new string('9', 64) };
        var actionLink = Relationship(PublishedRecordKind.Action, action.RecordId, '7');
        var voteLink = Relationship(PublishedRecordKind.Vote, vote.RecordId, '8') with
        {
            Type = PublishedRelationshipType.StatementAboutVote,
            Target = new() { Kind = PublishedRecordKind.Vote, RecordId = vote.RecordId, RevisionNumber = 3, Headline = "House passes the housing bill" }
        };
        var pages = new[]
        {
            await renderer.RenderRecordAsync(Record(), PublicationCatalogs.None, CancellationToken.None),
            await renderer.RenderStatementAsync(statement, [actionLink, voteLink], PublicationCatalogs.None, CancellationToken.None),
            await renderer.RenderActionAsync(action, [actionLink], PublicationCatalogs.None, CancellationToken.None),
            await renderer.RenderVoteAsync(vote, null, [voteLink], PublicationCatalogs.None, CancellationToken.None)
        };

        foreach (var html in pages)
        {
            var names = new HtmlParser().ParseDocument(html).QuerySelectorAll("main a")
                .Where(link => link.TextContent.StartsWith("Show in the full", StringComparison.Ordinal) || link.TextContent.StartsWith("Why they are linked", StringComparison.Ordinal))
                .Select(link => link.TextContent).ToList();

            Assert.NotEmpty(names);
            Assert.Equal(names.Count, names.Distinct().Count());
            Assert.All(names, name => Assert.Contains(": ", name, StringComparison.Ordinal));
        }
        var changeLinks = new HtmlParser().ParseDocument(pages[0]).QuerySelectorAll("main a").Select(link => link.TextContent).ToList();
        Assert.Contains("Show in the full version: earlier version, quote 1", changeLinks);
        Assert.Contains("Show in the full version: later version, quote 2", changeLinks);
        var statementLinks = new HtmlParser().ParseDocument(pages[1]).QuerySelectorAll("main a").ToList();
        Assert.Contains(statementLinks, link => link.TextContent == "Why they are linked: Housing bill funds rent aid");
        Assert.Contains(statementLinks, link => link.TextContent == "Why they are linked: House passes the housing bill");
        var quoteLinks = statementLinks
            .Where(link => link.TextContent.Contains(": statement, quote ", StringComparison.Ordinal)).ToList();
        Assert.Equal(statement.Quotations.Length, quoteLinks.Count);
        Assert.Equal(quoteLinks.Count, quoteLinks.Select(link => link.GetAttribute("href")).Distinct().Count());
    }

    [Fact]
    public async Task OnlyTheReportPageLoadsTheTurnstileScript()
    {
        var report = await new ReleaseRenderer("https://api.example.test", "site-key-1").RenderReportAsync(CancellationToken.None);
        var search = await new ReleaseRenderer("https://api.example.test", "site-key-1").RenderSearchAsync(ReleaseWithFilters(), SearchFacets(), CancellationToken.None);

        Assert.Contains("challenges.cloudflare.com/turnstile", report, StringComparison.Ordinal);
        Assert.DoesNotContain("challenges.cloudflare.com", search, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("https://api.example.test", null)]
    [InlineData(null, "site-key-1")]
    public async Task ReportPageWithoutTheServiceAddressOrTheSiteKeySaysThatReportingIsNotAvailableAndShowsNoFormOrKey(string? apiUrl, string? siteKey)
    {
        var html = await new ReleaseRenderer(apiUrl, siteKey).RenderReportAsync(CancellationToken.None);
        var document = new HtmlParser().ParseDocument(html);

        Assert.Contains("Reporting is not available", html, StringComparison.Ordinal);
        Assert.Null(document.QuerySelector("form"));
        Assert.DoesNotContain("data-sitekey", html, StringComparison.Ordinal);
        Assert.DoesNotContain("challenges.cloudflare.com", html, StringComparison.Ordinal);
        Assert.DoesNotContain("api.example.test", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AllRecordsPageListsRecordsWithRelativeLinks()
    {
        var release = new PublicationRelease
        {
            SchemaVersion = PublicationProtocol.SchemaVersion,
            ReleaseNumber = 4,
            PublishedAtUtc = new DateTimeOffset(2026, 5, 11, 8, 0, 0, TimeSpan.Zero),
            Withdrawals = [],
            Records =
            [
                new PublishedRecordEntry
                {
                    Kind = PublishedRecordKind.DocumentChange,
                    Officials = [],
                    IssueIds = [],
                    PolicyAreaIds = [],
                    RecordId = new string('c', 32),
                    RevisionNumber = 1,
                    Headline = "Headline <i>one</i>",
                    FirstPublishedAtUtc = new DateTimeOffset(2026, 5, 11, 8, 0, 0, TimeSpan.Zero)
                }
            ]
        };

        var html = await new ReleaseRenderer(null, null).RenderAllRecordsAsync(release, 1, CancellationToken.None);

        Assert.Contains($"href=\"records/{new string('c', 32)}.html\"", html, StringComparison.Ordinal);
        Assert.Contains("Headline &lt;i&gt;one&lt;/i&gt;", html, StringComparison.Ordinal);
        Assert.Contains("Release 4", html, StringComparison.Ordinal);
        Assert.Contains("href=\"assets/css/theme.css\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Private", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Page 1 of", html, StringComparison.Ordinal);
        Assert.DoesNotContain("rel=\"prev\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("rel=\"next\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StatementPageShowsSpeakersDateEvidenceKindLabelsAndMarksTheCitedSourceText()
    {
        var html = await new ReleaseRenderer(null, null).RenderStatementAsync(Statement(), [], PublicationCatalogs.None, CancellationToken.None);

        var document = new HtmlParser().ParseDocument(html);
        var facts = document.QuerySelectorAll(".facts dt").Zip(document.QuerySelectorAll(".facts dd"))
            .ToDictionary(pair => pair.First.TextContent, pair => pair.Second.TextContent);
        Assert.Equal("Jane <Doe>", facts["Speakers"]);
        Assert.Equal("Aide One", facts["Other speakers"]);
        Assert.Equal("2026-05-01", facts["Statement date"]);
        Assert.Equal("housing, transit", facts["Issues"]);
        Assert.Equal("None", facts["Policy areas"]);
        Assert.Equal("HB 12", facts["Bill numbers"]);
        Assert.Equal(["Direct quotation", "Release text"],
            document.QuerySelectorAll("[aria-labelledby='quotations-title'] .badge").Select(badge => badge.TextContent));
        Assert.Equal("<b>a rent cap</b>", document.QuerySelector("mark#cite-1")!.TextContent);
        Assert.Equal("Signed 2026-05-01.", document.QuerySelector("mark#cite-2")!.TextContent);
        Assert.Equal("Signed 2026-05-01.", document.QuerySelector("mark#cite-statement-date")!.TextContent);
        Assert.Contains("Evidence for the statement date", html, StringComparison.Ordinal);
        Assert.Contains("href=\"https://example.gov/story\"", html, StringComparison.Ordinal);
        Assert.Contains("Observed 2026-05-03 10:45 UTC", html, StringComparison.Ordinal);
        Assert.Contains("It does not show when the statement was first made.", html, StringComparison.Ordinal);
        Assert.DoesNotContain("when or why the source changed", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<script>", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RecordPagesLinkEachSpeakerToTheOfficialPageBecauseEverySpeakerOfAPublishedRecordHasOne()
    {
        var statement = Statement() with
        {
            Speakers = [new() { Id = "o1", Name = "Jane Doe" }, new() { Id = "o2", Name = "Ann Roe" }]
        };

        var html = await new ReleaseRenderer(null, null).RenderStatementAsync(statement, [], PublicationCatalogs.None, CancellationToken.None);

        var speakers = new HtmlParser().ParseDocument(html).QuerySelectorAll(".facts dd").First(dd => dd.TextContent.Contains("Jane Doe", StringComparison.Ordinal));
        Assert.Equal(["../officials/o1.html", "../officials/o2.html"], speakers.QuerySelectorAll("a").Select(link => link.GetAttribute("href")));
        Assert.Equal("Jane Doe, Ann Roe", speakers.TextContent);
    }

    [Fact]
    public async Task ActionPageLinksTheSponsorButNotACosponsorBecauseOnlyTheSponsorOwnsTheAction()
    {
        var html = await new ReleaseRenderer(null, null).RenderActionAsync(Action(), [], PublicationCatalogs.None, CancellationToken.None);

        var rows = new HtmlParser().ParseDocument(html).QuerySelectorAll("[aria-labelledby='sponsorships-title'] tbody tr").ToList();
        Assert.Equal("../officials/o1.html", rows.Single(row => row.QuerySelector("td")!.TextContent == "Jane <Doe>")
            .QuerySelector("td a")!.GetAttribute("href"));
        Assert.Null(rows.Single(row => row.QuerySelector("td")!.TextContent == "Ann Roe").QuerySelector("td a[href^='../officials/']"));
    }

    [Fact]
    public async Task VotePageLinksEachTrackedMemberToTheOfficialPage()
    {
        var html = await new ReleaseRenderer(null, null).RenderVoteAsync(Vote(), null, [], PublicationCatalogs.None, CancellationToken.None);

        var link = new HtmlParser().ParseDocument(html).QuerySelector("[aria-labelledby='members-title'] tbody td a")!;
        Assert.Equal("../officials/o1.html", link.GetAttribute("href"));
        Assert.Equal("Jane <Doe>", link.TextContent);
    }

    [Fact]
    public async Task StatementPageOmitsTheDateFactAndItsEvidenceWhenTheStatementHasNoEstablishedDate()
    {
        var html = await new ReleaseRenderer(null, null).RenderStatementAsync(
            Statement() with { StatementDate = null, StatementDateEvidence = null }, [], PublicationCatalogs.None, CancellationToken.None);

        Assert.DoesNotContain("Statement date", html, StringComparison.Ordinal);
        Assert.DoesNotContain("cite-statement-date", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StatementPageRejectsAQuoteThatIsNotTheCitedSourceText()
    {
        var statement = Statement() with { Quotations = [Statement().Quotations[0] with { Quote = "invented words" }] };

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new ReleaseRenderer(null, null).RenderStatementAsync(statement, [], PublicationCatalogs.None, CancellationToken.None));
    }

    [Fact]
    public async Task ActionPageShowsTheBillTheSponsorshipTableTheCrsSummaryAndTheSources()
    {
        var html = await new ReleaseRenderer(null, null).RenderActionAsync(Action(), [], HousingCatalog, CancellationToken.None);

        var document = new HtmlParser().ParseDocument(html);
        var facts = document.QuerySelectorAll(".facts dt").Zip(document.QuerySelectorAll(".facts dd"))
            .ToDictionary(pair => pair.First.TextContent, pair => pair.Second.TextContent);
        Assert.Equal("HR 7, Congress 119", facts["Bill"]);
        Assert.Equal("A <housing> bill", facts["Title"]);
        Assert.Equal("Housing and Community Development", facts["Policy area"]);
        Assert.Equal("Housing costs", facts["Tracked issues"]);
        var rows = document.QuerySelectorAll("[aria-labelledby='sponsorships-title'] tbody tr")
            .Select(row => string.Join("|", row.QuerySelectorAll("td").Select(cell => cell.QuerySelector("blockquote")?.TextContent ?? cell.TextContent.Trim())));
        Assert.Equal(["Jane <Doe>|Sponsor|No|2026-01-03|None|Sponsor: D000001", "Ann Roe|Cosponsor|Yes|2026-01-05|2026-01-20|Cosponsor: R000002, original"], rows);
        Assert.Contains("Summary version: 00 <i>funds</i> rent aid.", document.QuerySelector("[aria-labelledby='bill-evidence-title']")!.TextContent,
            StringComparison.Ordinal);
        var sources = document.QuerySelector("[aria-labelledby='sources-title']")!;
        Assert.Contains("https://api.congress.gov/v3/bill/119/hr/7/cosponsors", sources.TextContent, StringComparison.Ordinal);
        Assert.Empty(sources.QuerySelectorAll("a"));
        Assert.Equal(["Sponsor: D000001", "Policy area: Housing and Community Development", "Summary version: 00 <i>funds</i> rent aid.",
            "Cosponsor: R000002, original"], sources.QuerySelectorAll("mark").Select(mark => mark.TextContent));
        Assert.Contains("A sponsorship is a proposal, not a law.", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<i>", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ActionAndVotePagesSayThatTheRecordComesFromTheOfficialRecordInsteadOfClaimingAPersonApprovedIt()
    {
        var renderer = new ReleaseRenderer(null, null);

        foreach (var html in new[] { await renderer.RenderActionAsync(Action(), [], PublicationCatalogs.None, CancellationToken.None),
                     await renderer.RenderVoteAsync(Vote(), null, [], PublicationCatalogs.None, CancellationToken.None) })
        {
            Assert.Contains("From the official record, checked", html, StringComparison.Ordinal);
            Assert.DoesNotContain("Approved 20", html, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task ActionAndVotePagesLabelAnAiSummaryOnlyWhenAModelWroteTheText()
    {
        var renderer = new ReleaseRenderer(null, null);
        var action = Action();
        var vote = Vote();
        var person = PublishedTextOrigin.Person;

        var model = new[] { await renderer.RenderActionAsync(action, [], PublicationCatalogs.None, CancellationToken.None),
            await renderer.RenderVoteAsync(vote, null, [], PublicationCatalogs.None, CancellationToken.None) };
        var written = new[] { await renderer.RenderActionAsync(action with { TextOrigin = person }, [], PublicationCatalogs.None, CancellationToken.None),
            await renderer.RenderVoteAsync(vote with { TextOrigin = person }, null, [], PublicationCatalogs.None, CancellationToken.None) };

        Assert.All(model, html => Assert.Contains("An AI summary of the official record", html, StringComparison.Ordinal));
        Assert.All(written, html => Assert.DoesNotContain("AI summary", html, StringComparison.Ordinal));
    }

    [Fact]
    public async Task EveryActionQuoteLinksToTheMarkOfTheSameTextInTheSourceAndEachSourceIsLabelledByPosition()
    {
        var html = await new ReleaseRenderer(null, null).RenderActionAsync(Action(), [], PublicationCatalogs.None, CancellationToken.None);

        var document = new HtmlParser().ParseDocument(html);
        var links = document.QuerySelectorAll("a[href^='#cite-']").ToArray();
        Assert.Equal(4, links.Length);
        foreach (var link in links)
        {
            var quote = link.Closest("td, .choice")!.QuerySelector("blockquote")!.TextContent;
            Assert.Equal(quote, document.QuerySelector(link.GetAttribute("href")!)!.TextContent);
        }
        Assert.Equal(["Source text 1 of 2", "Source text 2 of 2"],
            document.QuerySelectorAll("[aria-labelledby='sources-title'] [aria-label]").Select(item => item.GetAttribute("aria-label")));
    }

    [Fact]
    public async Task ActionPageRejectsACitationThatNamesNoEmbeddedSource()
    {
        var action = Action() with { SummaryCitation = Action().SummaryCitation! with { ExtractionId = new string('9', 64) } };

        await Assert.ThrowsAsync<InvalidDataException>(() => new ReleaseRenderer(null, null).RenderActionAsync(action, [], PublicationCatalogs.None, CancellationToken.None));
    }

    [Fact]
    public async Task VotePageShowsTheDayTheKindTheResultFactsTheCastOfEachTrackedMemberAndTheSources()
    {
        var html = await new ReleaseRenderer(null, null).RenderVoteAsync(Vote(), null, [], HousingCatalog, CancellationToken.None);

        var document = new HtmlParser().ParseDocument(html);
        var facts = document.QuerySelectorAll(".facts dt").Zip(document.QuerySelectorAll(".facts dd"))
            .ToDictionary(pair => pair.First.TextContent, pair => pair.Second.TextContent);
        Assert.Equal("2025-04-10", facts["Voted on"]);
        Assert.Equal("Passage", facts["Kind"]);
        Assert.Equal("House", facts["Chamber"]);
        Assert.Equal("100, Congress 119, session 1", facts["Roll call"]);
        Assert.Equal("Housing costs", facts["Tracked issues"]);
        Assert.Equal("Passed <b>", facts["Result"]);
        var result = document.QuerySelector("[aria-labelledby='result-title']")!;
        Assert.Equal(["Started: 2025-04-10T11:08:00-04:00", "Question: On Passage", "Result: Passed <b>"],
            result.QuerySelectorAll("blockquote").Select(quote => quote.TextContent));
        var rows = document.QuerySelectorAll("[aria-labelledby='members-title'] tbody tr")
            .Select(row => string.Join("|", row.QuerySelectorAll("td").Select(cell => cell.QuerySelector("blockquote")?.TextContent ?? cell.TextContent.Trim())));
        Assert.Equal(["Jane <Doe>|Yea|Member: A000055, Jane Doe, R, AL, Yea"], rows);
        var sources = document.QuerySelector("[aria-labelledby='sources-title']")!;
        Assert.Equal(["Source text 1 of 2", "Source text 2 of 2"], sources.QuerySelectorAll("[aria-label]").Select(item => item.GetAttribute("aria-label")));
        Assert.Contains("only the officials that Civic Lens tracks", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<b>", html, StringComparison.Ordinal);
    }

    // A bare count could mean the tracked members or all members who voted Yea, so each total names the whole body and what it counts.
    [Theory]
    [InlineData(PublishedChamber.House, "Casts of all House members: Yea 218, Nay 210, Present 1, Not voting 5")]
    [InlineData(PublishedChamber.Senate, "Casts of all senators: Yea 218, Nay 210, Present 1, Not voting 5")]
    public async Task TheVoteTotalsNameTheWholeBodyThatTheyCount(PublishedChamber chamber, string expected)
    {
        var vote = Vote() with
        {
            Chamber = chamber,
            Totals = new() { Yea = 218, Nay = 210, Present = 1, NotVoting = 5 },
            PartyMajorities = [PublishedPartyMajority.Of("Republican", 218, 210)]
        };

        Assert.Equal(expected, (await VoteFactsOf(vote))["Totals"]);
    }

    [Fact]
    public async Task ASpeakerElectionShowsItsCandidateTotalsAndNoYeaAndNayLine()
    {
        var vote = Vote("Candidate total Emmer: 1", "Candidate total Johnson (LA): 218") with { Totals = null, PartyMajorities = [] };

        var facts = await VoteFactsOf(vote);

        Assert.Equal("Votes for each candidate from all House members: Emmer 1, Johnson (LA) 218", facts["Totals"]);
        Assert.DoesNotContain("Yea", facts["Totals"], StringComparison.Ordinal);
    }

    private static async Task<Dictionary<string, string>> VoteFactsOf(PublishedVote vote)
    {
        var html = await new ReleaseRenderer(null, null).RenderVoteAsync(vote, null, [], PublicationCatalogs.None, CancellationToken.None);
        var document = new HtmlParser().ParseDocument(html);
        return document.QuerySelectorAll(".facts dt").Zip(document.QuerySelectorAll(".facts dd"))
            .ToDictionary(pair => pair.First.TextContent, pair => pair.Second.TextContent);
    }

    [Theory]
    [InlineData(PublishedChamber.House, "House Clerk roll call")]
    [InlineData(PublishedChamber.Senate, "Senate roll call")]
    public async Task TheVoteCaveatNamesTheSourceOfTheChamberThatHeldTheVote(PublishedChamber chamber, string source)
    {
        var html = await new ReleaseRenderer(null, null).RenderVoteAsync(Vote() with { Chamber = chamber }, null, [], PublicationCatalogs.None, CancellationToken.None);

        Assert.Contains(source, new HtmlParser().ParseDocument(html).QuerySelector(".caveat")!.TextContent, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(PublishedVoteKind.Nomination, "Nomination")]
    [InlineData(PublishedVoteKind.Cloture, "Cloture")]
    [InlineData(PublishedVoteKind.MotionToProceed, "Motion to proceed")]
    public async Task ASenateVoteKindHasItsPublicLabelOnThePage(PublishedVoteKind kind, string label)
    {
        var html = await new ReleaseRenderer(null, null).RenderVoteAsync(Vote() with { Kind = kind }, null, [], PublicationCatalogs.None, CancellationToken.None);

        var document = new HtmlParser().ParseDocument(html);
        var facts = document.QuerySelectorAll(".facts dt").Zip(document.QuerySelectorAll(".facts dd"))
            .ToDictionary(pair => pair.First.TextContent, pair => pair.Second.TextContent);
        Assert.Equal(label, facts["Kind"]);
    }

    [Fact]
    public async Task EveryVoteQuoteLinksToTheMarkOfTheSameTextInTheSource()
    {
        var html = await new ReleaseRenderer(null, null).RenderVoteAsync(Vote(), null, [], PublicationCatalogs.None, CancellationToken.None);

        var document = new HtmlParser().ParseDocument(html);
        var links = document.QuerySelectorAll("a[href^='#cite-']").ToArray();
        Assert.Equal(4, links.Length);
        foreach (var link in links)
        {
            var quote = link.Closest("td, .choice")!.QuerySelector("blockquote")!.TextContent;
            Assert.Equal(quote, document.QuerySelector(link.GetAttribute("href")!)!.TextContent);
        }
    }

    [Fact]
    public async Task TheVotePageLinksToTheBillActionOnlyWhenTheReleaseHoldsThatAction()
    {
        var actionId = new string('c', 64);

        var linked = new HtmlParser().ParseDocument(await new ReleaseRenderer(null, null).RenderVoteAsync(Vote(), actionId, [], PublicationCatalogs.None, CancellationToken.None));
        var plain = new HtmlParser().ParseDocument(await new ReleaseRenderer(null, null).RenderVoteAsync(Vote(), null, [], PublicationCatalogs.None, CancellationToken.None));

        var link = Assert.Single(linked.QuerySelectorAll("[aria-labelledby='bill-title'] a"));
        Assert.Equal(($"../records/{actionId}.html", "HR 7, Congress 119"), (link.GetAttribute("href"), link.TextContent));
        Assert.Empty(plain.QuerySelectorAll("[aria-labelledby='bill-title'] a"));
        Assert.Contains("HR 7, Congress 119", plain.QuerySelector("[aria-labelledby='bill-title']")!.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AVoteWithoutABillShowsNoBillPanel()
    {
        var html = await new ReleaseRenderer(null, null).RenderVoteAsync(Vote() with { BillType = null, BillNumber = null }, null, [], PublicationCatalogs.None, CancellationToken.None);

        Assert.Empty(new HtmlParser().ParseDocument(html).QuerySelectorAll("[aria-labelledby='bill-title']"));
    }

    [Fact]
    public async Task VotePageRejectsACitationThatNamesNoEmbeddedSource()
    {
        var vote = Vote() with { VotedOnCitation = Vote().VotedOnCitation with { ExtractionId = new string('9', 64) } };

        await Assert.ThrowsAsync<InvalidDataException>(() => new ReleaseRenderer(null, null).RenderVoteAsync(vote, null, [], PublicationCatalogs.None, CancellationToken.None));
    }

    [Fact]
    public async Task EachKindOfCitedRecordListsItsRelatedRecordsAsLinksWithTheLinkTypeAndTheStatementDate()
    {
        var statement = Statement();
        var action = Action();
        var vote = Vote() with { RecordId = new string('9', 64) };
        var actionLink = Relationship(PublishedRecordKind.Action, action.RecordId, '7');
        var voteLink = Relationship(PublishedRecordKind.Vote, vote.RecordId, '8') with { Type = PublishedRelationshipType.StatementAboutVote };
        var renderer = new ReleaseRenderer(null, null);

        var statementPage = new HtmlParser().ParseDocument(await renderer.RenderStatementAsync(statement, [actionLink, voteLink], PublicationCatalogs.None, CancellationToken.None));
        var actionPage = new HtmlParser().ParseDocument(await renderer.RenderActionAsync(action, [actionLink], PublicationCatalogs.None, CancellationToken.None));
        var votePage = new HtmlParser().ParseDocument(await renderer.RenderVoteAsync(vote, null, [voteLink], PublicationCatalogs.None, CancellationToken.None));

        var rows = statementPage.QuerySelectorAll("[aria-labelledby='related-title'] li");
        Assert.Equal(2, rows.Length);
        var targetLink = rows[0].QuerySelectorAll("a");
        Assert.Equal(($"../records/{action.RecordId}.html", "Housing bill funds rent aid"), (targetLink[0].GetAttribute("href"), targetLink[0].TextContent));
        Assert.Equal($"../records/{actionLink.RecordId}.html", targetLink[1].GetAttribute("href"));
        Assert.Contains("Action, Describes own bill, statement of 2026-05-01", rows[0].TextContent, StringComparison.Ordinal);
        Assert.Contains("Vote, Statement about a vote", rows[1].TextContent, StringComparison.Ordinal);
        var onAction = Assert.Single(actionPage.QuerySelectorAll("[aria-labelledby='related-title'] li"));
        Assert.Equal(($"../records/{statement.RecordId}.html", "Doe backs a rent cap"), (onAction.QuerySelector("a")!.GetAttribute("href"), onAction.QuerySelector("a")!.TextContent));
        var onVote = Assert.Single(votePage.QuerySelectorAll("[aria-labelledby='related-title'] li"));
        Assert.Equal($"../records/{statement.RecordId}.html", onVote.QuerySelector("a")!.GetAttribute("href"));
    }

    [Fact]
    public async Task APageWithoutRelatedRecordsShowsNoRelatedPanel()
    {
        var renderer = new ReleaseRenderer(null, null);

        Assert.Empty(new HtmlParser().ParseDocument(await renderer.RenderStatementAsync(Statement(), [], PublicationCatalogs.None, CancellationToken.None))
            .QuerySelectorAll("[aria-labelledby='related-title']"));
        Assert.Empty(new HtmlParser().ParseDocument(await renderer.RenderActionAsync(Action(), [], PublicationCatalogs.None, CancellationToken.None))
            .QuerySelectorAll("[aria-labelledby='related-title']"));
        Assert.Empty(new HtmlParser().ParseDocument(await renderer.RenderVoteAsync(Vote(), null, [], PublicationCatalogs.None, CancellationToken.None))
            .QuerySelectorAll("[aria-labelledby='related-title']"));
    }

    [Fact]
    public async Task RelationshipPageNamesBothCitedRevisionsLinksTheirPagesEncodesTheApprovedEvidenceAndStatesNoVerdict()
    {
        var link = Relationship(PublishedRecordKind.Action, new string('a', 64), '7');

        var html = await new ReleaseRenderer(null, null).RenderRelationshipAsync(link, PublicationCatalogs.None, CancellationToken.None);

        var document = new HtmlParser().ParseDocument(html);
        Assert.DoesNotContain("<b>", html, StringComparison.Ordinal);
        Assert.Equal(link.Headline, document.QuerySelector("h1")!.TextContent);
        var cited = document.QuerySelectorAll("[aria-labelledby='linked-title'] li");
        Assert.Equal(["Statement, revision 2", "Action, revision 3"],
            cited.Select(row => row.QuerySelector("span")!.TextContent.Trim()));
        Assert.Equal([$"../records/{link.Statement.RecordId}.html", $"../records/{link.Target.RecordId}.html"],
            cited.Select(row => row.QuerySelector("a")!.GetAttribute("href")));
        Assert.Equal(["<b>a rent cap</b>", "Title: A housing bill"],
            document.QuerySelectorAll("[aria-labelledby='evidence-title'] blockquote").Select(quote => quote.TextContent));
        Assert.Contains("states no verdict", document.QuerySelector(".caveat")!.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RelationshipPageRejectsATypeThatDoesNotFitTheKindOfTheTarget()
    {
        var link = Relationship(PublishedRecordKind.Action, new string('a', 64), '7') with { Type = PublishedRelationshipType.StatementAboutVote };

        await Assert.ThrowsAsync<InvalidDataException>(() => new ReleaseRenderer(null, null).RenderRelationshipAsync(link, PublicationCatalogs.None, CancellationToken.None));
    }

    [Fact]
    public async Task BriefPageShowsEachCitationAsALinkToItsRecordPageWithTheCopiedHeadlineTheWeekTheCutoffAndTheSampleNote()
    {
        var html = await new ReleaseRenderer(null, null).RenderBriefAsync(Brief(), CancellationToken.None);

        var document = new HtmlParser().ParseDocument(html);
        Assert.Equal("A week of <housing> news", document.QuerySelector("h1")!.TextContent);
        var facts = document.QuerySelectorAll(".facts dt").Zip(document.QuerySelectorAll(".facts dd"))
            .ToDictionary(pair => pair.First.TextContent, pair => pair.Second.TextContent);
        Assert.Equal("Starts Monday 2026-10-05, New York time", facts["Week"]);
        Assert.Equal("3", facts["Cutoff release"]);
        Assert.Equal(("5", "4", "2"), (facts["Records considered"], facts["Records sent to the drafting model"], facts["Records cited"]));
        var citations = document.QuerySelectorAll("[aria-labelledby='citations-title'] li")
            .Select(item => (item.Id, item.QuerySelector("a")!.GetAttribute("href"), item.QuerySelector("a")!.TextContent)).ToArray();
        Assert.Equal([("citation-R1", $"../records/{new string('1', 64)}.html", "Rent cap <statement>"),
            ("citation-R2", $"../records/{new string('2', 32)}.html", "Zoning rule change")], citations);
        var paragraphs = document.QuerySelectorAll("[aria-labelledby='brief-title'] p")
            .Select(paragraph => paragraph.QuerySelectorAll("a").Select(link => link.GetAttribute("href")).ToArray()).ToArray();
        Assert.Equal([["#citation-R1"], ["#citation-R1", "#citation-R2"]], paragraphs);
        Assert.Contains("This brief is a sample, not a full account of the week. It cites 2 of the 5 new records", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<housing>", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BriefPageRejectsAParagraphThatCitesAnIdTheBriefDoesNotList()
    {
        var brief = Brief();

        await Assert.ThrowsAsync<InvalidDataException>(() => new ReleaseRenderer(null, null).RenderBriefAsync(
            brief with { Paragraphs = [.. brief.Paragraphs, new() { Text = "More.", CitationIds = ["R9"] }] }, CancellationToken.None));
    }

    [Fact]
    public async Task AllRecordsPageLabelsEachEntryWithItsKindSoReadersKnowWhatTheyOpen()
    {
        var release = ReleaseOf(2);
        release = release with
        {
            Records = [release.Records[0], release.Records[1] with { Kind = PublishedRecordKind.Statement, RecordId = new string('d', 64) },
                release.Records[1] with { Kind = PublishedRecordKind.Action, RecordId = new string('f', 64) }]
        };

        var html = await new ReleaseRenderer(null, null).RenderAllRecordsAsync(release, 1, CancellationToken.None);

        var rows = new HtmlParser().ParseDocument(html).QuerySelectorAll("tbody tr")
            .Select(row => (row.QuerySelector("a")!.GetAttribute("href"), row.QuerySelector(".badge")!.TextContent));
        Assert.Equal([($"records/{release.Records[0].RecordId}.html", "Document change"),
            ($"records/{new string('d', 64)}.html", "Statement"), ($"records/{new string('f', 64)}.html", "Action")], rows);
    }

    [Theory]
    [InlineData(1, 50, 0, "Page 1 of 3", false, "all-2.html")]
    [InlineData(2, 50, 50, "Page 2 of 3", true, "all-3.html")]
    [InlineData(3, 20, 100, "Page 3 of 3", true, null)]
    public async Task AllRecordsPageShowsOnlyItsSliceInManifestOrderWithPagerLinksWhereTheyApply(
        int page, int expectedCount, int firstIndex, string label, bool hasPrevious, string? next)
    {
        var release = ReleaseOf(120);

        var html = await new ReleaseRenderer(null, null).RenderAllRecordsAsync(release, page, CancellationToken.None);

        var shown = Regex.Matches(html, "href=\"records/([0-9a-f]{32})\\.html\"").Select(match => match.Groups[1].Value).ToList();
        Assert.Equal(release.Records.Skip(firstIndex).Take(expectedCount).Select(entry => entry.RecordId), shown);
        Assert.Contains(label, html, StringComparison.Ordinal);
        var document = new HtmlParser().ParseDocument(html);
        var previous = page == 2 ? "all.html" : "all-2.html";
        Assert.Equal(hasPrevious ? previous : null, document.QuerySelector("a[rel='prev']")?.GetAttribute("href"));
        Assert.Equal(next, document.QuerySelector("a[rel='next']")?.GetAttribute("href"));
        Assert.Contains("href=\"assets/css/theme.css\"", html, StringComparison.Ordinal);
        Assert.DoesNotMatch(new Regex("(?:href|src)=\"(?:/|//|https?:)"), html);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    public async Task AllRecordsPageRejectsAPageOutsideTheRelease(int page)
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            new ReleaseRenderer(null, null).RenderAllRecordsAsync(ReleaseOf(120), page, CancellationToken.None));
    }

    [Fact]
    public async Task HomeShowsTheBriefWithCitationLinksToItsPageThenOnlyTheTwentyNewestRecords()
    {
        var release = ReleaseOf(30);
        var brief = Brief();

        var html = await new ReleaseRenderer(null, null).RenderHomeAsync(release, brief, NoFacets, CancellationToken.None);

        var document = new HtmlParser().ParseDocument(html);
        Assert.Equal("A week of <housing> news", document.QuerySelector("h2")!.TextContent);
        Assert.Equal(["Warren backed a rent cap.", "A rule changed."],
            document.QuerySelectorAll("p.record-body").Select(paragraph => paragraph.FirstChild!.TextContent.Trim()));
        var briefPage = $"records/{brief.RecordId}.html";
        Assert.Equal([$"{briefPage}#citation-R1", $"{briefPage}#citation-R1", $"{briefPage}#citation-R2"],
            document.QuerySelectorAll("p.record-body a").Select(link => link.GetAttribute("href")));
        Assert.Equal(release.Records.Take(20).Select(entry => $"records/{entry.RecordId}.html"),
            document.QuerySelectorAll("tbody a.cell-title").Select(link => link.GetAttribute("href")));
        Assert.NotNull(document.QuerySelector("a[href='all.html']"));
    }

    [Fact]
    public async Task HomeSaysSoWhenNoBriefExistsInsteadOfShowingAnEmptyPanel()
    {
        var html = await new ReleaseRenderer(null, null).RenderHomeAsync(ReleaseOf(1), null, NoFacets, CancellationToken.None);

        Assert.Contains("No briefing yet", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HomeAndTheFacetListLinkOnlyFacetsThatHavePagesAndShowNamesAndCounts()
    {
        var release = ReleaseOf(3) with { Records = [.. ReleaseOf(3).Records.Select(entry => entry with { IssueIds = ["housing", "Not A Path"] })] };
        var facets = ReleaseFacets.From(release, new PublicationCatalog(new Dictionary<string, string>(), IssuesNamed("housing", "Housing costs")));

        var home = await new ReleaseRenderer(null, null).RenderHomeAsync(release, null, facets, CancellationToken.None);
        var list = await new ReleaseRenderer(null, null).RenderFacetsAsync(facets, CancellationToken.None);

        foreach (var html in new[] { home, list })
        {
            var rows = new HtmlParser().ParseDocument(html).QuerySelectorAll("tbody tr").ToList();
            var housing = rows.Single(row => row.TextContent.Contains("Housing costs", StringComparison.Ordinal));
            Assert.Equal("issues/housing.html", housing.QuerySelector("a")!.GetAttribute("href"));
            Assert.Contains("3", housing.TextContent, StringComparison.Ordinal);
            var unsafeId = rows.Single(row => row.TextContent.Contains("Not A Path", StringComparison.Ordinal));
            Assert.Null(unsafeId.QuerySelector("a"));
        }
    }

    [Fact]
    public async Task FacetPageLinksResolveFromItsSubfolderAndPageThroughItsRecordsNewestEventDateFirst()
    {
        var release = ReleaseOf(120) with
        {
            Records = [.. ReleaseOf(120).Records.Select((entry, index) => entry with
        {
            PolicyAreaIds = ["land-use"],
            FirstPublishedAtUtc = new DateTimeOffset(2026, 5, 11, 8, 0, 0, TimeSpan.Zero),
            EventDate = new DateOnly(2026, 1, 1).AddDays(index)
        })]
        };
        var facet = ReleaseFacets.From(release, PublicationCatalogs.None).PolicyAreas.Single();

        var html = await new ReleaseRenderer(null, null).RenderFacetPageAsync(facet, 2, CancellationToken.None);

        var document = new HtmlParser().ParseDocument(html);
        Assert.Equal(release.Records.Reverse().Skip(50).Take(50).Select(entry => $"../records/{entry.RecordId}.html"),
            document.QuerySelectorAll("tbody a.cell-title").Select(link => link.GetAttribute("href")));
        Assert.Equal("../policy-areas/land-use.html", document.QuerySelector("a[rel='prev']")!.GetAttribute("href"));
        Assert.Equal("../policy-areas/land-use.page-3.html", document.QuerySelector("a[rel='next']")!.GetAttribute("href"));
        Assert.Contains("href=\"../assets/css/theme.css\"", html, StringComparison.Ordinal);
        Assert.Contains("href=\"../index.html\"", html, StringComparison.Ordinal);
        Assert.Contains("href=\"../issues.html\"", html, StringComparison.Ordinal);
        Assert.DoesNotMatch(new Regex("(?:href|src)=\"(?:/|//|https?:)"), html);
    }

    [Fact]
    public async Task FacetPageShowsTheEventDateAndOfficialsOfEachEntryAndFallsBackToTheFirstPublicationDayWhenThereIsNoEventDateAndSameDayEntriesFollowTheLaterPublicationFirst()
    {
        PublishedRecordEntry Entry(char id, DateTimeOffset published, DateOnly? eventDate, params string[] officials) => new()
        {
            Kind = PublishedRecordKind.DocumentChange,
            PolicyAreaIds = [],
            RecordId = new string(id, 32),
            RevisionNumber = 1,
            Headline = $"Headline {id}",
            FirstPublishedAtUtc = published,
            EventDate = eventDate,
            IssueIds = ["housing"],
            Officials = [.. officials.Select(name => new PublishedOfficial { Id = name.ToLowerInvariant(), Name = name })]
        };
        var release = ReleaseOf(0) with
        {
            Withdrawals = [],
            Records =
        [
            Entry('a', new DateTimeOffset(2026, 5, 11, 8, 0, 0, TimeSpan.Zero), new DateOnly(2026, 1, 2), "Jane Doe", "Ann Roe"),
            Entry('b', new DateTimeOffset(2026, 5, 9, 21, 0, 0, TimeSpan.FromHours(-5)), null, "Jane Doe"),
            Entry('c', new DateTimeOffset(2026, 5, 12, 8, 0, 0, TimeSpan.Zero), null),
            Entry('d', new DateTimeOffset(2026, 5, 10, 20, 0, 0, TimeSpan.Zero), null, "Ann Roe")
        ]
        };
        var facet = ReleaseFacets.From(release, PublicationCatalogs.None).Issues.Single();

        var html = await new ReleaseRenderer(null, null).RenderFacetPageAsync(facet, 1, CancellationToken.None);

        var rows = new HtmlParser().ParseDocument(html).QuerySelectorAll("tbody tr")
            .Select(row => string.Join("|", row.QuerySelectorAll("td").Select(cell => cell.TextContent.Trim())));
        Assert.Equal(["Headline c Document change||2026-05-12", "Headline d Document change|Ann Roe|2026-05-10",
            "Headline b Document change|Jane Doe|2026-05-10", "Headline a Document change|Jane Doe, Ann Roe|2026-01-02"], rows);
    }

    [Fact]
    public async Task EveryRecordKindListsItsDatedEventsInDateOrderOnATimeline()
    {
        var statement = await TimelineOf(renderer => renderer.RenderStatementAsync(Statement(), [], PublicationCatalogs.None, CancellationToken.None));
        var change = await TimelineOf(renderer => renderer.RenderRecordAsync(Record(), PublicationCatalogs.None, CancellationToken.None));
        var action = await TimelineOf(renderer => renderer.RenderActionAsync(Action(), [], PublicationCatalogs.None, CancellationToken.None));
        var vote = await TimelineOf(renderer => renderer.RenderVoteAsync(Vote(), null, [], PublicationCatalogs.None, CancellationToken.None));
        var relationship = await TimelineOf(renderer => renderer.RenderRelationshipAsync(
            Relationship(PublishedRecordKind.Action, new string('a', 64), '7') with { StatementDate = new DateOnly(2026, 2, 3) }, PublicationCatalogs.None, CancellationToken.None));
        var brief = await TimelineOf(renderer => renderer.RenderBriefAsync(Brief(), CancellationToken.None));

        Assert.Equal(["2026-05-01|Statement made", "2026-05-03|Source observed, 2026-05-03 10:45 UTC",
            "2026-05-10|Approved, 2026-05-10 12:00 UTC", "2026-05-11|First published, 2026-05-11 08:00 UTC"], statement);
        Assert.Equal(["2026-03-01|The source changed", "2026-04-02|Earlier version observed, 2026-04-02 09:30 UTC",
            "2026-05-03|Later version observed, 2026-05-03 10:45 UTC", "2026-05-10|Approved, 2026-05-10 12:00 UTC",
            "2026-05-11|First published, 2026-05-11 08:00 UTC"], change);
        Assert.Equal(["2026-01-03|Sponsored by Jane <Doe> (sponsor)", "2026-01-05|Sponsored by Ann Roe (cosponsor)",
            "2026-01-20|Withdrawn by Ann Roe (cosponsor)", "2026-01-22|Approved, 2026-01-22 12:00 UTC",
            "2026-01-23|First published, 2026-01-23 08:00 UTC"], action);
        Assert.Equal(["2025-04-10|Vote held", "2026-01-22|Approved, 2026-01-22 12:00 UTC", "2026-01-23|First published, 2026-01-23 08:00 UTC"], vote);
        Assert.Equal(["2026-02-03|Statement made", "2026-02-04|Approved, 2026-02-04 12:00 UTC", "2026-02-05|First published, 2026-02-05 08:00 UTC"],
            relationship);
        Assert.Equal(["2026-10-05|Week starts", "2026-10-06|Approved, 2026-10-06 12:00 UTC", "2026-10-07|First published, 2026-10-07 08:00 UTC"], brief);
    }

    [Fact]
    public async Task AReplacedRecordShowsEachCorrectionWithItsNoteOrTheUpdatedFromTheSourceLabelAndAddsItToTheTimeline()
    {
        var corrections = new PublishedCorrection[]
        {
            new() { ReplacedRevisionNumber = 2, PublishedAtUtc = new DateTimeOffset(2026, 2, 4, 9, 0, 0, TimeSpan.Zero), Note = "Fixed the <sponsor> date." },
            new() { ReplacedRevisionNumber = 1, PublishedAtUtc = new DateTimeOffset(2026, 2, 2, 9, 0, 0, TimeSpan.Zero) }
        };
        var action = Action() with { RevisionNumber = 3, Corrections = corrections };
        var statement = Statement() with { RevisionNumber = 3, Corrections = corrections };
        var renderer = new ReleaseRenderer(null, null);

        foreach (var html in new[] { await renderer.RenderActionAsync(action, [], PublicationCatalogs.None, CancellationToken.None),
                     await renderer.RenderStatementAsync(statement, [], PublicationCatalogs.None, CancellationToken.None) })
        {
            var entries = new HtmlParser().ParseDocument(html).QuerySelectorAll("ul.corrections > li")
                .Select(item => (item.QuerySelector("strong")!.TextContent, item.QuerySelector("p")?.TextContent)).ToArray();
            Assert.Equal(("Correction, 2026-02-04. Replaces revision 2.", "Fixed the <sponsor> date."), entries[0]);
            Assert.Equal(("Updated from the source, 2026-02-02. Replaces revision 1.", null), entries[1]);
        }
        var timeline = await TimelineOf(renderer => renderer.RenderActionAsync(action, [], PublicationCatalogs.None, CancellationToken.None));
        Assert.Contains("2026-02-04|Correction, replaces revision 2, 2026-02-04 09:00 UTC", timeline);
        Assert.Contains("2026-02-02|Updated from the source, replaces revision 1, 2026-02-02 09:00 UTC", timeline);
    }

    [Fact]
    public async Task AWithdrawalNoticeShowsTheKindTheRevisionTheDatesTheEscapedNoteAndTheHistoryButNoRecordContent()
    {
        var withdrawal = new PublishedWithdrawal
        {
            Kind = PublishedRecordKind.Action,
            RecordId = new string('c', 64),
            WithdrawnRevisionNumber = 3,
            FirstPublishedAtUtc = new DateTimeOffset(2026, 2, 1, 8, 0, 0, TimeSpan.Zero),
            WithdrawnAtUtc = new DateTimeOffset(2026, 2, 9, 9, 30, 0, TimeSpan.Zero),
            Note = "Wrong <bill>.",
            Corrections = [new() { ReplacedRevisionNumber = 2, PublishedAtUtc = new DateTimeOffset(2026, 2, 4, 9, 0, 0, TimeSpan.Zero), Note = "Fixed a date." }]
        };

        var html = await new ReleaseRenderer(null, null).RenderWithdrawalAsync(withdrawal, CancellationToken.None);

        var document = new HtmlParser().ParseDocument(html);
        var text = document.QuerySelector("main")!.TextContent;
        Assert.Equal("This record was withdrawn", document.QuerySelector("h1")!.TextContent);
        foreach (var expected in new[] { "Action", new string('c', 64), "3", "2026-02-01 08:00 UTC", "2026-02-09 09:30 UTC", "Wrong <bill>." })
            Assert.Contains(expected, text, StringComparison.Ordinal);
        Assert.Contains("Wrong &lt;bill&gt;.", html, StringComparison.Ordinal);
        Assert.Equal("Correction, 2026-02-04. Replaces revision 2.", document.QuerySelector("ul.corrections > li strong")!.TextContent);
        Assert.Empty(document.QuerySelectorAll("main a[href*='report.html?']"));
    }

    [Fact]
    public async Task AReleaseWithNoRecordLeftStillRendersItsIndexPagesSoAWithdrawalOfTheLastRecordPublishes()
    {
        var release = ReleaseOf(0);
        var facets = ReleaseFacets.From(release, PublicationCatalogs.None);
        var renderer = new ReleaseRenderer(null, null);

        Assert.NotEmpty(await renderer.RenderHomeAsync(release, null, facets, CancellationToken.None));
        Assert.NotEmpty(await renderer.RenderAllRecordsAsync(release, 1, CancellationToken.None));
        Assert.NotEmpty(await renderer.RenderSearchAsync(release, facets, CancellationToken.None));
        Assert.NotEmpty(await renderer.RenderFacetsAsync(facets, CancellationToken.None));
    }

    [Fact]
    public async Task ARecordWithNoCorrectionsShowsNoCorrectionList()
    {
        var html = await new ReleaseRenderer(null, null).RenderActionAsync(Action(), [], PublicationCatalogs.None, CancellationToken.None);

        Assert.Empty(new HtmlParser().ParseDocument(html).QuerySelectorAll("ul.corrections"));
    }

    [Fact]
    public async Task TimesOnOneDayKeepTheirClockOrderSoTheShownTimesNeverContradictTheOrder()
    {
        var vote = Vote() with
        {
            ApprovedAtUtc = new DateTimeOffset(2026, 1, 22, 12, 0, 0, TimeSpan.Zero),
            FirstPublishedAtUtc = new DateTimeOffset(2026, 1, 22, 8, 0, 0, TimeSpan.Zero)
        };

        var events = await TimelineOf(renderer => renderer.RenderVoteAsync(vote, null, [], PublicationCatalogs.None, CancellationToken.None));

        Assert.Equal(["2025-04-10|Vote held", "2026-01-22|First published, 2026-01-22 08:00 UTC", "2026-01-22|Approved, 2026-01-22 12:00 UTC"], events);
    }

    private static async Task<string[]> TimelineOf(Func<ReleaseRenderer, Task<string>> render)
    {
        var html = await render(new ReleaseRenderer(null, null));
        return [.. new HtmlParser().ParseDocument(html).QuerySelectorAll("[aria-labelledby='timeline-title'] tbody tr")
            .Select(row => string.Join("|", row.QuerySelectorAll("td").Select(cell => cell.TextContent.Trim())))];
    }

    [Fact]
    public async Task FacetPageRejectsAPageOutsideTheFacet()
    {
        var release = ReleaseOf(3) with { Records = [.. ReleaseOf(3).Records.Select(entry => entry with { IssueIds = ["housing"] })] };
        var facet = ReleaseFacets.From(release, PublicationCatalogs.None).Issues.Single();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => new ReleaseRenderer(null, null).RenderFacetPageAsync(facet, 2, CancellationToken.None));
    }

    [Fact]
    public async Task PagesOfOneListHaveDifferentTitlesSoSearchResultsAndTabsCanTellThemApart()
    {
        var renderer = new ReleaseRenderer(null, null);
        var release = ReleaseOf(120) with { Records = [.. ReleaseOf(120).Records.Select(entry => entry with { PolicyAreaIds = ["land-use"] })] };
        var facet = ReleaseFacets.From(release, PublicationCatalogs.None).PolicyAreas.Single();

        var titles = new List<string>();
        foreach (var page in Enumerable.Range(1, 3))
        {
            titles.Add(TitleOf(await renderer.RenderAllRecordsAsync(release, page, CancellationToken.None)));
            titles.Add(TitleOf(await renderer.RenderFacetPageAsync(facet, page, CancellationToken.None)));
        }

        Assert.Equal(titles.Count, titles.Distinct().Count());
        Assert.Equal("All published records, page 2 of 3 | Civic Lens", titles[2]);
    }

    [Fact]
    public async Task APagedTitleKeepsItsPageNumberWhenTheNameIsLongAndOnePageHasNoPageNumber()
    {
        var name = new string('x', 70);
        var release = ReleaseOf(120) with { Records = [.. ReleaseOf(120).Records.Select(entry => entry with { IssueIds = ["housing"] })] };
        var facet = ReleaseFacets.From(release, new PublicationCatalog(new Dictionary<string, string>(), IssuesNamed("housing", name))).Issues.Single();
        var renderer = new ReleaseRenderer(null, null);

        Assert.Equal($"{name}, page 3 of 3 | Civic Lens", TitleOf(await renderer.RenderFacetPageAsync(facet, 3, CancellationToken.None)));
        Assert.Equal("All published records | Civic Lens", TitleOf(await renderer.RenderAllRecordsAsync(ReleaseOf(3), 1, CancellationToken.None)));
    }

    [Fact]
    public async Task TwoWithdrawalsHaveDifferentTitlesSoTabsAndSearchResultsCanTellThemApart()
    {
        PublishedWithdrawal Withdrawal(char id) => new()
        {
            Kind = PublishedRecordKind.Action,
            RecordId = new string(id, 64),
            WithdrawnRevisionNumber = 1,
            FirstPublishedAtUtc = DateTimeOffset.UnixEpoch,
            WithdrawnAtUtc = DateTimeOffset.UnixEpoch,
            Note = "Wrong.",
            Corrections = []
        };
        var renderer = new ReleaseRenderer(null, null);

        var first = TitleOf(await renderer.RenderWithdrawalAsync(Withdrawal('a'), CancellationToken.None));
        var second = TitleOf(await renderer.RenderWithdrawalAsync(Withdrawal('b'), CancellationToken.None));

        Assert.NotEqual(first, second);
        Assert.Equal("Withdrawn action: aaaaaaaaaaaa | Civic Lens", first);
    }

    [Fact]
    public async Task ALongSummaryWithNewlinesAndSpaceRunsIsCutAtAWholeWordOnOneLineAndOpenGraphRepeatsIt()
    {
        var summary = string.Join("\n  ", Enumerable.Repeat("housing   rules\nchanged", 20));
        var html = await new ReleaseRenderer(null, null).RenderRecordAsync(Record() with { Summary = summary }, PublicationCatalogs.None, CancellationToken.None);

        var head = new HtmlParser().ParseDocument(html).Head!;
        var description = head.QuerySelector("meta[name='description']")!.GetAttribute("content")!;
        Assert.InRange(description.Length, 100, 160);
        Assert.EndsWith("…", description, StringComparison.Ordinal);
        Assert.Matches(@"(housing|rules|changed)…$", description);
        Assert.DoesNotMatch(@"\s{2}|[\r\n]", description);
        Assert.Equal(description, head.QuerySelector("meta[property='og:description']")!.GetAttribute("content"));
    }

    [Fact]
    public async Task AWordlessHeadlineIsNeverCutBetweenTheTwoHalvesOfASurrogatePair()
    {
        var html = await new ReleaseRenderer(null, null).RenderRecordAsync(Record() with { Headline = string.Concat(Enumerable.Repeat("😀", 60)) }, PublicationCatalogs.None, CancellationToken.None);

        var title = TitleOf(html);
        Assert.DoesNotContain('\uFFFD', title);
        Assert.StartsWith("😀😀", title, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EveryPageTypeHasABoundedDescriptionThatOpenGraphRepeatsAndNoCanonicalLink()
    {
        var pages = (await RenderEveryPageTypeAsync()).Select(page => page.Html);

        foreach (var html in pages)
        {
            var head = new HtmlParser().ParseDocument(html).Head!;
            var description = head.QuerySelector("meta[name='description']")!.GetAttribute("content")!;
            Assert.InRange(description.Length, 10, 160);
            Assert.Equal(description, head.QuerySelector("meta[property='og:description']")!.GetAttribute("content"));
            Assert.EndsWith(" | Civic Lens", head.QuerySelector("title")!.TextContent, StringComparison.Ordinal);
            Assert.Equal(head.QuerySelector("title")!.TextContent, head.QuerySelector("meta[property='og:title']")!.GetAttribute("content"));
            Assert.Equal("Civic Lens", head.QuerySelector("meta[property='og:site_name']")!.GetAttribute("content"));
            Assert.Contains("favicon.svg", head.QuerySelector("link[rel='icon']")!.GetAttribute("href"), StringComparison.Ordinal);
            Assert.Null(head.QuerySelector("link[rel='canonical']"));
            Assert.Null(head.QuerySelector("meta[property='og:url']"));
        }
    }

    // Written by ReleaseIndexPages and ReleaseOfficialPages. PublishReleaseTests pins them.
    private static readonly string[] WrittenRootPages =
    [
        PublicationProtocol.HomePath, PublicationProtocol.OfficialsPagePath, PublicationProtocol.FacetsPath,
        PublicationProtocol.AllRecordsPagePath(1), PublicationProtocol.SearchPagePath, PublicationProtocol.MethodPagePath,
        PublicationProtocol.ReportPagePath
    ];

    [Fact]
    public async Task EveryPageTypeMarksItsSectionInTheNavigationOrNoneWhenItBelongsToNoSection()
    {
        foreach (var (name, _, html, section) in await RenderEveryPageTypeAsync())
        {
            var current = new HtmlParser().ParseDocument(html).QuerySelectorAll("nav[aria-label='Site'] [aria-current]");

            Assert.True(section is null ? current.Length == 0 : current.Length == 1 && current[0].TextContent == section, name);
        }
    }

    [Fact]
    public async Task EveryNavigationAndFooterLinkOfEveryPageTypeLeadsToAPageTheReleaseWritesSoNoReaderReachesA404()
    {
        foreach (var (name, pagePath, html, _) in await RenderEveryPageTypeAsync())
        {
            var document = new HtmlParser().ParseDocument(html);
            var links = document.QuerySelectorAll("nav[aria-label='Site'] a, nav[aria-label='Footer'] a")
                .Select(link => link.GetAttribute("href")!).ToList();

            Assert.Equal(6, links.Count);
            Assert.All(links, href => Assert.True(WrittenRootPages.Contains(ReleasePath(pagePath, href)), $"{name}: {href}"));
        }
    }

    private static async Task<(string Name, string Path, string Html, string? Section)[]> RenderEveryPageTypeAsync()
    {
        var renderer = new ReleaseRenderer(null, null);
        var release = ReleaseWithFilters();
        var facets = SearchFacets();
        var relationship = Relationship(PublishedRecordKind.Action, new string('a', 64), '7');
        var withdrawal = new PublishedWithdrawal
        {
            Kind = PublishedRecordKind.Action,
            RecordId = new string('c', 64),
            WithdrawnRevisionNumber = 3,
            FirstPublishedAtUtc = DateTimeOffset.UnixEpoch,
            WithdrawnAtUtc = DateTimeOffset.UnixEpoch,
            Note = "Wrong.",
            Corrections = []
        };
        var profile = new PublishedOfficialProfile
        {
            Id = "mayor",
            Name = "Mayor Example",
            Terms = [new() { Office = "Representative", Party = "Republican", Start = new DateOnly(2025, 1, 3) }]
        };
        PublishedRecordEntry[] entries =
        [
            new()
            {
                Kind = PublishedRecordKind.Statement,
                RecordId = new string('1', 64),
                RevisionNumber = 1,
                Headline = "A statement",
                FirstPublishedAtUtc = DateTimeOffset.UnixEpoch,
                EventDate = new DateOnly(2026, 3, 1),
                IssueIds = ["housing"],
                PolicyAreaIds = [],
                Officials = [new() { Id = "mayor", Name = "Mayor Example" }]
            }
        ];
        PublishedOfficialVote[] votes =
        [
            new()
            {
                RecordId = new string('3', 64),
                VotedOn = new DateOnly(2026, 4, 1),
                Chamber = PublishedChamber.House,
                Congress = 119,
                Session = 1,
                RollCallNumber = 100,
                BillType = "hr",
                BillNumber = 7,
                Cast = "Yea",
                Result = "Passed",
                Totals = new() { Yea = 218, Nay = 210, Present = 1, NotVoting = 5 },
                SplitVote = false,
                Party = "Republican",
                Headline = "A vote",
                Kind = PublishedVoteKind.Passage,
                IssueIds = ["housing"]
            }
        ];
        var official = new ReleaseOfficialPage(profile, true, 119, true, [], [.. entries], [.. votes], TalkAndActionProfile.Of(119, entries, votes));
        var pastOfficial = new ReleaseOfficialPage(profile, true, 118, false, [], [], []);
        return
        [
            ("record", PublicationProtocol.RecordPagePath(Record().RecordId), await renderer.RenderRecordAsync(Record(), PublicationCatalogs.None, CancellationToken.None), "All records"),
            ("statement", PublicationProtocol.RecordPagePath(Statement().RecordId), await renderer.RenderStatementAsync(Statement(), [], PublicationCatalogs.None, CancellationToken.None), "All records"),
            ("action", PublicationProtocol.RecordPagePath(Action().RecordId), await renderer.RenderActionAsync(Action(), [], PublicationCatalogs.None, CancellationToken.None), "All records"),
            ("vote", PublicationProtocol.RecordPagePath(Vote().RecordId), await renderer.RenderVoteAsync(Vote(), null, [], PublicationCatalogs.None, CancellationToken.None), "All records"),
            ("relationship", PublicationProtocol.RecordPagePath(relationship.RecordId), await renderer.RenderRelationshipAsync(relationship, PublicationCatalogs.None, CancellationToken.None), "All records"),
            ("brief", PublicationProtocol.RecordPagePath(Brief().RecordId), await renderer.RenderBriefAsync(Brief(), CancellationToken.None), "All records"),
            ("withdrawal", PublicationProtocol.RecordPagePath(withdrawal.RecordId), await renderer.RenderWithdrawalAsync(withdrawal, CancellationToken.None), "All records"),
            ("home", PublicationProtocol.HomePath, await renderer.RenderHomeAsync(release, null, facets, CancellationToken.None), null),
            ("all records", PublicationProtocol.AllRecordsPagePath(1), await renderer.RenderAllRecordsAsync(release, 1, CancellationToken.None), "All records"),
            ("issue", facets.Issues[0].PagePath(1), await renderer.RenderFacetPageAsync(facets.Issues[0], 1, CancellationToken.None), "Issues"),
            ("policy area", facets.PolicyAreas[0].PagePath(1), await renderer.RenderFacetPageAsync(facets.PolicyAreas[0], 1, CancellationToken.None), "Issues"),
            ("issues", PublicationProtocol.FacetsPath, await renderer.RenderFacetsAsync(facets, CancellationToken.None), "Issues"),
            ("official", official.PagePath, await renderer.RenderOfficialAsync(official, PublicationCatalogs.None, CancellationToken.None), "Officials"),
            ("past congress", pastOfficial.PagePath, await renderer.RenderOfficialAsync(pastOfficial, PublicationCatalogs.None, CancellationToken.None), "Officials"),
            ("officials", PublicationProtocol.OfficialsPagePath, await renderer.RenderOfficialsAsync([new OfficialListing(profile, 0)], DateTimeOffset.UnixEpoch, CancellationToken.None), "Officials"),
            ("search", PublicationProtocol.SearchPagePath, await renderer.RenderSearchAsync(release, facets, CancellationToken.None), "Search"),
            ("method", PublicationProtocol.MethodPagePath, await renderer.RenderMethodAsync(CancellationToken.None), null),
            ("report", PublicationProtocol.ReportPagePath, await renderer.RenderReportAsync(CancellationToken.None), null)
        ];
    }

    [Fact]
    public async Task ABreadcrumbTrailResolvesFromEachFolderDepthAndMarksOnlyTheLastItemAsTheCurrentPage()
    {
        var pages = (await RenderEveryPageTypeAsync()).ToDictionary(page => page.Name);

        AssertTrail(pages["all records"], ["Home|index.html", "All records"]);
        AssertTrail(pages["issues"], ["Home|index.html", "Issues and policy areas"]);
        AssertTrail(pages["search"], ["Home|index.html", "Search records"]);
        AssertTrail(pages["officials"], ["Home|index.html", "Officials"]);
        AssertTrail(pages["method"], ["Home|index.html", "How the official page counts work"]);
        AssertTrail(pages["report"], ["Home|index.html", "Report a problem"]);
        AssertTrail(pages["issue"], ["Home|../index.html", "Issues|../issues.html", "Housing costs"]);
        AssertTrail(pages["vote"], ["Home|../index.html", "All records|../all.html", Vote().Headline]);
        AssertTrail(pages["withdrawal"], ["Home|../index.html", "All records|../all.html", "Withdrawn action: cccccccccccc"]);
        AssertTrail(pages["official"], ["Home|../index.html", "Officials|../officials.html", "Mayor Example"]);
        AssertTrail(pages["past congress"], ["Home|../../index.html", "Officials|../../officials.html",
            "Mayor Example|../../officials/mayor.html", "Mayor Example, Congress 118"]);
        foreach (var page in pages.Values)
            Assert.Equal(page.Name != "home", new HtmlParser().ParseDocument(page.Html).QuerySelector("nav[aria-label='Breadcrumb']") is not null);
    }

    [Fact]
    public async Task EveryBreadcrumbLinkOfEveryPageTypeLeadsToAPageTheReleaseWritesSoNoReaderReachesA404()
    {
        foreach (var (name, pagePath, html, _) in await RenderEveryPageTypeAsync())
        {
            var links = new HtmlParser().ParseDocument(html).QuerySelectorAll("nav[aria-label='Breadcrumb'] a")
                .Select(link => link.GetAttribute("href")!).ToList();

            Assert.All(links, href => Assert.True(
                WrittenRootPages.Append(PublicationProtocol.OfficialPagePath("mayor")).Contains(ReleasePath(pagePath, href)), $"{name}: {href}"));
        }
    }

    [Fact]
    public async Task ARecordPageShowsTheConfiguredIssueNameLinkedToItsIssuePageAndAnUnconfiguredIdAsPlainText()
    {
        var renderer = new ReleaseRenderer(null, null);
        var action = Action() with { TrackedIssueIds = ["housing", "mystery"] };
        var vote = Vote() with { TrackedIssueIds = ["housing", "mystery"] };
        var statement = Statement() with { IssueIds = ["housing", "mystery"] };

        var change = Record() with { IssueIds = ["housing", "mystery"] };
        var relationship = Relationship(PublishedRecordKind.Action, new string('a', 64), '7') with { IssueIds = ["housing", "mystery"] };

        foreach (var html in new[] { await renderer.RenderActionAsync(action, [], HousingCatalog, CancellationToken.None),
                     await renderer.RenderVoteAsync(vote, null, [], HousingCatalog, CancellationToken.None),
                     await renderer.RenderStatementAsync(statement, [], HousingCatalog, CancellationToken.None),
                     await renderer.RenderRecordAsync(change, HousingCatalog, CancellationToken.None),
                     await renderer.RenderRelationshipAsync(relationship, HousingCatalog, CancellationToken.None) })
        {
            var issues = FactValue(html, "Tracked issues", "Issues");

            Assert.Equal("Housing costs, mystery", Regex.Replace(issues.TextContent, @"\s+", " ").Trim());
            AssertSingleLink(issues, "../" + PublicationProtocol.IssuePagePath("housing", 1), "Housing costs");
        }
    }

    [Fact]
    public async Task AnActionStatementAndRelationshipPageShowTheConfiguredPolicyAreaNameLinkedToItsPageAndAnUnconfiguredIdAsPlainText()
    {
        var renderer = new ReleaseRenderer(null, null);
        var statement = Statement() with { PolicyAreaIds = ["land-use", "mystery"] };
        var relationship = Relationship(PublishedRecordKind.Action, new string('a', 64), '7') with { PolicyAreaIds = ["land-use", "mystery"] };

        foreach (var html in new[] { await renderer.RenderStatementAsync(statement, [], HousingCatalog, CancellationToken.None),
                     await renderer.RenderRelationshipAsync(relationship, HousingCatalog, CancellationToken.None) })
        {
            var areas = FactValue(html, "Policy areas");

            Assert.Equal("Land use, mystery", Regex.Replace(areas.TextContent, @"\s+", " ").Trim());
            AssertSingleLink(areas, "../" + PublicationProtocol.PolicyAreaPagePath("land-use", 1), "Land use");
        }

        var configured = await renderer.RenderActionAsync(Action() with { PolicyAreaId = "land-use" }, [], HousingCatalog, CancellationToken.None);
        AssertSingleLink(FactValue(configured, "Policy area"), "../" + PublicationProtocol.PolicyAreaPagePath("land-use", 1), "Housing and Community Development");

        var unconfigured = await renderer.RenderActionAsync(Action() with { PolicyAreaId = "mystery" }, [], HousingCatalog, CancellationToken.None);
        var plain = FactValue(unconfigured, "Policy area");
        Assert.Equal("Housing and Community Development", plain.TextContent.Trim());
        Assert.Empty(plain.QuerySelectorAll("a"));
    }

    private static AngleSharp.Dom.IElement FactValue(string html, params string[] labels) =>
        new HtmlParser().ParseDocument(html).QuerySelectorAll(".facts dt").Single(term => labels.Contains(term.TextContent)).NextElementSibling!;

    private static void AssertSingleLink(AngleSharp.Dom.IElement scope, string href, string text)
    {
        var link = Assert.Single(scope.QuerySelectorAll("a"));
        Assert.Equal(href, link.GetAttribute("href"));
        Assert.Equal(text, link.TextContent);
    }

    private static void AssertTrail((string Name, string Path, string Html, string? Section) page, string[] expected)
    {
        var items = new HtmlParser().ParseDocument(page.Html).QuerySelectorAll("nav[aria-label='Breadcrumb'] ol > li").ToList();

        Assert.Equal(expected, items.Select(item => item.QuerySelector("a") is { } link ? $"{link.TextContent}|{link.GetAttribute("href")}" : item.TextContent.Trim()));
        Assert.Equal(["page"], items.SelectMany(item => item.QuerySelectorAll("[aria-current]")).Select(mark => mark.GetAttribute("aria-current")));
        Assert.Equal(items[^1].QuerySelector("[aria-current]"), items[^1].Children.Single());
        Assert.All(items.SelectMany(item => item.QuerySelectorAll("span[aria-hidden]")), mark => Assert.Equal("/", mark.TextContent));
    }

    [Fact]
    public async Task EveryPublicTableKeepsItsTableRolesSoWebKitReadersKeepRowsAndHeadersWhenCssStacksTheCells()
    {
        var tables = new Dictionary<string, int>();
        foreach (var (name, _, html, _) in await RenderEveryPageTypeAsync())
        {
            foreach (var table in new HtmlParser().ParseDocument(html).QuerySelectorAll("table.table"))
            {
                tables[name] = tables.GetValueOrDefault(name) + 1;
                Assert.True(table.GetAttribute("role") == "table", $"{name}: table role");
                Assert.All(table.QuerySelectorAll("thead, tbody"), group => Assert.Equal("rowgroup", group.GetAttribute("role")));
                Assert.All(table.QuerySelectorAll("tr"), row => Assert.Equal("row", row.GetAttribute("role")));
                var headers = table.QuerySelectorAll("thead th").ToList();
                Assert.All(headers, header => Assert.Equal(("columnheader", "col"), (header.GetAttribute("role"), header.GetAttribute("scope"))));
                foreach (var row in table.QuerySelectorAll("tbody tr"))
                {
                    var cells = row.Children;
                    Assert.Equal(headers.Count, cells.Length);
                    Assert.True(cells.Count(cell => cell.GetAttribute("role") == "rowheader") == 1, $"{name}: rowheader count");
                    Assert.All(cells, cell => Assert.Contains(cell.GetAttribute("role"), new[] { "cell", "rowheader" }));
                    Assert.All(cells.Where(cell => cell.GetAttribute("role") == "cell"), cell => Assert.NotNull(cell.GetAttribute("data-label")));
                }
            }
        }

        var expected = new Dictionary<string, int>
        {
            ["action"] = 2,
            ["all records"] = 1,
            ["brief"] = 1,
            ["home"] = 3,
            ["issue"] = 1,
            ["issues"] = 2,
            ["official"] = 3,
            ["policy area"] = 1,
            ["record"] = 1,
            ["statement"] = 1,
            ["vote"] = 2
        };
        Assert.All(expected, page => Assert.True(tables.GetValueOrDefault(page.Key) >= page.Value, $"{page.Key}: tables"));
    }

    [Fact]
    public async Task EveryCellOfAPublicTableCarriesItsColumnHeaderAsLabelSoAPhoneReaderSeesWhatEachValueIs()
    {
        var labeled = new Dictionary<string, int>();
        foreach (var (name, _, html, _) in await RenderEveryPageTypeAsync())
        {
            foreach (var table in new HtmlParser().ParseDocument(html).QuerySelectorAll("table.table"))
            {
                var headers = table.QuerySelectorAll("thead th").Select(header => header.TextContent).ToList();
                foreach (var row in table.QuerySelectorAll("tbody tr"))
                {
                    var cells = row.QuerySelectorAll("td");
                    for (var index = 0; index < cells.Length; index++)
                    {
                        if (cells[index].GetAttribute("role") == "rowheader")
                        {
                            Assert.True(cells[index].GetAttribute("data-label") is null, $"{name}: main cell of {headers[index]}");
                            continue;
                        }
                        Assert.True(headers[index] == cells[index].GetAttribute("data-label"), $"{name}: column {headers[index]}");
                        labeled[name] = labeled.GetValueOrDefault(name) + 1;
                    }
                }
            }
        }

        Assert.True(labeled.GetValueOrDefault("official") >= 10);
        Assert.True(labeled.Values.Sum() >= 16);
    }

    [Fact]
    public async Task ARecordPageUsesItsHeadlineAsTitleCutAtAWordAndIsAnArticle()
    {
        var headline = string.Join(' ', Enumerable.Repeat("housing", 30));
        var html = await new ReleaseRenderer(null, null).RenderRecordAsync(Record() with { Headline = headline }, PublicationCatalogs.None, CancellationToken.None);

        var head = new HtmlParser().ParseDocument(html).Head!;
        var title = head.QuerySelector("title")!.TextContent;
        Assert.InRange(title.Length, 20, 70 + " | Civic Lens".Length);
        Assert.StartsWith("housing housing", title, StringComparison.Ordinal);
        Assert.Contains("housing… | Civic Lens", title, StringComparison.Ordinal);
        Assert.Equal("article", head.QuerySelector("meta[property='og:type']")!.GetAttribute("content"));
    }

    private static string TitleOf(string html) => new HtmlParser().ParseDocument(html).QuerySelector("title")!.TextContent;

    [Fact]
    public void AssetsIncludeTheThemeAtItsReleaseRelativePath()
    {
        var assets = new ReleaseRenderer(null, null).Assets;

        foreach (var path in new[] { "assets/css/theme.css", "assets/css/app.css", "assets/css/public.css", "assets/js/theme.js", "assets/js/app.js", "assets/js/public.js",
                     "assets/fonts/instrument-sans.woff2", "assets/fonts/newsreader.woff2", "assets/favicon.svg" })
            Assert.Contains(assets, asset => asset.RelativePath == path && asset.Content.Length > 0);
    }

    [Fact]
    public async Task EveryPageTypePreloadsInstrumentSansAndOnlyPagesWithNewsreaderTextAboveTheFoldPreloadNewsreader()
    {
        string[] newsreaderPages = ["record", "statement", "action", "vote", "relationship", "brief", "withdrawal"];

        foreach (var (name, pagePath, html, _) in await RenderEveryPageTypeAsync())
        {
            string[] expected = newsreaderPages.Contains(name) ? [InstrumentSansFont, NewsreaderFont] : [InstrumentSansFont];
            Assert.True(expected.SequenceEqual(FontPreloads(html, pagePath)), name);
        }
    }

    [Fact]
    public async Task TheHomePagePreloadsNewsreaderWhenItShowsABriefBecauseTheBriefTextIsSetInNewsreader()
    {
        var html = await new ReleaseRenderer(null, null).RenderHomeAsync(ReleaseOf(1), Brief(), NoFacets, CancellationToken.None);

        Assert.Equal([InstrumentSansFont, NewsreaderFont], FontPreloads(html, PublicationProtocol.HomePath));
    }

    private const string InstrumentSansFont = "assets/fonts/instrument-sans.woff2";
    private const string NewsreaderFont = "assets/fonts/newsreader.woff2";

    private static string[] FontPreloads(string html, string pagePath)
    {
        var assets = new ReleaseRenderer(null, null).Assets;
        var preloads = new HtmlParser().ParseDocument(html).Head!.QuerySelectorAll("link[rel='preload']").ToArray();
        Assert.All(preloads, link =>
        {
            Assert.Equal("font", link.GetAttribute("as"));
            Assert.Equal("font/woff2", link.GetAttribute("type"));
            Assert.True(link.HasAttribute("crossorigin"));
        });
        var paths = preloads.Select(link => ReleasePath(pagePath, link.GetAttribute("href")!)).ToArray();
        Assert.All(paths, path => Assert.Contains(assets, asset => asset.RelativePath == path));
        return paths;
    }

    [Fact]
    public void ReviewStylesAndScriptsStayOutOfTheRelease()
    {
        var assets = new ReleaseRenderer(null, null).Assets;
        Assert.DoesNotContain(assets, asset => asset.RelativePath.EndsWith("review.css", StringComparison.Ordinal));
        Assert.DoesNotContain(assets, asset => asset.RelativePath.EndsWith("review.js", StringComparison.Ordinal));
    }

    [Fact]
    public async Task EveryStylesheetAndScriptAPageLinksIsAReleaseAssetWhetherThePageIsAtTheRootOrInAFolder()
    {
        var renderer = new ReleaseRenderer(null, null);
        var rootPage = await renderer.RenderMethodAsync(CancellationToken.None);
        var folderPage = await renderer.RenderRecordAsync(Record(), PublicationCatalogs.None, CancellationToken.None);

        AssertLinkedAssetsAreReleaseAssets(renderer, rootPage, "method.html");
        AssertLinkedAssetsAreReleaseAssets(renderer, folderPage, "records/record.html");
    }

    [Fact]
    public async Task APublicPageNeverUsesAClassOnlyTheReviewStylesheetDefinesBecauseTheReleaseDoesNotShipIt()
    {
        var shipped = HostStyleSheets.ClassNames(HostStyleSheets.Read("app.css"));
        shipped.UnionWith(HostStyleSheets.ClassNames(HostStyleSheets.Read("public.css")));
        var reviewOnly = HostStyleSheets.ClassNames(HostStyleSheets.Read("review.css")).Except(shipped).ToHashSet();

        var withForms = new ReleaseRenderer("https://api.example.test", "site-key-1");
        (string Name, string Html)[] formPages =
        [
            ("report form", await withForms.RenderReportAsync(CancellationToken.None)),
            ("search form", await withForms.RenderSearchAsync(ReleaseWithFilters(), SearchFacets(), CancellationToken.None))
        ];
        foreach (var (name, html) in (await RenderEveryPageTypeAsync()).Select(page => (page.Name, page.Html)).Concat(formPages))
        {
            var used = new HtmlParser().ParseDocument(html).QuerySelectorAll("[class]").SelectMany(element => element.ClassList).ToHashSet();
            Assert.Empty(used.Intersect(reviewOnly).Select(className => $"{name} uses .{className}"));
        }
    }

    private static void AssertLinkedAssetsAreReleaseAssets(ReleaseRenderer renderer, string html, string pagePath)
    {
        var links = new HtmlParser().ParseDocument(html).QuerySelectorAll("link[rel=stylesheet], script[src]")
            .Select(element => element.GetAttribute("href") ?? element.GetAttribute("src")!)
            .ToArray();
        Assert.Contains(links, link => link.EndsWith("assets/css/public.css", StringComparison.Ordinal));
        Assert.Contains(links, link => link.EndsWith("assets/js/public.js", StringComparison.Ordinal));
        foreach (var link in links)
            Assert.Contains(renderer.Assets, asset => asset.RelativePath == ReleasePath(pagePath, link));
    }

    private static string ReleasePath(string pagePath, string href) => new Uri(new Uri("https://x/" + pagePath), href).AbsolutePath[1..];

    private static readonly ReleaseFacets NoFacets = new([], []);

    private static PublicationCatalog HousingCatalog { get; } = new(new Dictionary<string, string>(), IssuesNamed("housing", "Housing costs"));

    private static IssueConfiguration IssuesNamed(string id, string name) => new()
    {
        PolicyAreas = [new() { Id = "land-use", Name = "Land use" }],
        Issues = [new() { Id = id, Name = name, Definition = "d", PolicyAreaIds = ["land-use"] }]
    };

    private static ReleaseFacets SearchFacets() => ReleaseFacets.From(ReleaseWithFilters(), new PublicationCatalog(new Dictionary<string, string>(),
        new IssueConfiguration
        {
            PolicyAreas = [new() { Id = "area-one", Name = "Area One" }],
            Issues =
            [
                new() { Id = "housing", Name = "Housing costs", Definition = "d", PolicyAreaIds = ["area-one"] },
                new() { Id = "transit", Name = "Transit", Definition = "d", PolicyAreaIds = ["area-one"] }
            ]
        }));

    private static PublicationRelease ReleaseWithFilters() => new()
    {
        SchemaVersion = PublicationProtocol.SchemaVersion,
        ReleaseNumber = 4,
        PublishedAtUtc = new DateTimeOffset(2026, 5, 11, 8, 0, 0, TimeSpan.Zero),
        Withdrawals = [],
        Records =
        [
            new PublishedRecordEntry
            {
                Kind = PublishedRecordKind.DocumentChange,
                PolicyAreaIds = [],
                RecordId = new string('c', 32), RevisionNumber = 1, Headline = "One", FirstPublishedAtUtc = DateTimeOffset.UnixEpoch,
                Officials = [new PublishedOfficial { Id = "o2", Name = "Jane Doe" }], IssueIds = ["transit"], SourceHost = "b.gov"
            },
            new PublishedRecordEntry
            {
                Kind = PublishedRecordKind.DocumentChange,
                RecordId = new string('d', 32), RevisionNumber = 1, Headline = "Two", FirstPublishedAtUtc = DateTimeOffset.UnixEpoch,
                Officials = [new PublishedOfficial { Id = "o1", Name = "Ann Roe" }], IssueIds = ["housing", "transit"],
                PolicyAreaIds = ["area-one"], SourceHost = "a.gov"
            }
        ]
    };

    private static PublicationRelease ReleaseOf(int count) => new()
    {
        SchemaVersion = PublicationProtocol.SchemaVersion,
        ReleaseNumber = 4,
        PublishedAtUtc = new DateTimeOffset(2026, 5, 11, 8, 0, 0, TimeSpan.Zero),
        Withdrawals = [],
        Records = [.. Enumerable.Range(0, count).Select(index => new PublishedRecordEntry
        {
            IssueIds = [],
            Kind = PublishedRecordKind.DocumentChange,
            Officials = [],
            PolicyAreaIds = [],
            RecordId = index.ToString("x32"),
            RevisionNumber = 1,
            Headline = $"Headline {index}",
            FirstPublishedAtUtc = new DateTimeOffset(2026, 5, 11, 8, 0, 0, TimeSpan.Zero).AddMinutes(-index)
        })]
    };

    private static string MarkedText(string html, string id)
    {
        var match = Regex.Match(html, $"<mark id=\"{id}\">(.*?)</mark>", RegexOptions.Singleline);
        Assert.True(match.Success, $"No mark with id {id}.");
        return match.Groups[1].Value;
    }

    private static PublishedRelationship Relationship(PublishedRecordKind targetKind, string targetId, char id) => new()
    {
        Corrections = [],
        RecordId = new string(id, 64),
        RevisionNumber = 1,
        Headline = "Doe backs a rent cap and Housing bill funds rent aid",
        Statement = new() { Kind = PublishedRecordKind.Statement, RecordId = Statement().RecordId, RevisionNumber = 2, Headline = "Doe backs a rent cap" },
        Target = new() { Kind = targetKind, RecordId = targetId, RevisionNumber = 3, Headline = "Housing bill funds rent aid" },
        Type = PublishedRelationshipType.DescribesOwnBill,
        Reason = "Doe says the bill is hers.",
        Quote = "<b>a rent cap</b>",
        TargetFact = "Title: A housing bill",
        StatementDate = new DateOnly(2026, 5, 1),
        MatchedOfficialId = "o1",
        Officials = [new PublishedOfficial { Id = "o1", Name = "Jane <Doe>" }],
        IssueIds = ["housing"],
        PolicyAreaIds = [],
        ApprovedAtUtc = new DateTimeOffset(2026, 2, 4, 12, 0, 0, TimeSpan.Zero),
        FirstPublishedAtUtc = new DateTimeOffset(2026, 2, 5, 8, 0, 0, TimeSpan.Zero)
    };

    private static PublishedBrief Brief() => new()
    {
        Corrections = [],
        RecordId = CivicLens.Core.Briefs.BriefWeek.RecordIdOf(new DateOnly(2026, 10, 5)),
        RevisionNumber = 1,
        Headline = "A week of <housing> news",
        WeekStart = new DateOnly(2026, 10, 5),
        CutoffReleaseNumber = 3,
        Coverage = new() { Considered = 5, SentToModel = 4, Cited = 2 },
        Paragraphs =
        [
            new() { Text = "Warren backed a rent cap.", CitationIds = ["R1"] },
            new() { Text = "A rule changed.", CitationIds = ["R1", "R2"] }
        ],
        Citations =
        [
            new() { Id = "R1", Kind = PublishedRecordKind.Statement, RecordId = new string('1', 64), RevisionNumber = 1, Headline = "Rent cap <statement>" },
            new() { Id = "R2", Kind = PublishedRecordKind.DocumentChange, RecordId = new string('2', 32), RevisionNumber = 3, Headline = "Zoning rule change" }
        ],
        ApprovedAtUtc = new DateTimeOffset(2026, 10, 6, 12, 0, 0, TimeSpan.Zero),
        FirstPublishedAtUtc = new DateTimeOffset(2026, 10, 7, 8, 0, 0, TimeSpan.Zero)
    };

    private static PublishedAction Action()
    {
        const string detail = "Sponsor: D000001\nPolicy area: Housing and Community Development\nSummary version: 00 <i>funds</i> rent aid.\n";
        const string cosponsors = "Cosponsor: R000002, original\n";
        var detailId = new string('1', 64);
        var cosponsorsId = new string('2', 64);
        PublishedCitation Cite(string id, string text, string quote) =>
            new() { ExtractionId = id, Start = text.IndexOf(quote, StringComparison.Ordinal), Length = quote.Length, Quote = quote };
        PublishedDocumentVersion Source(string id, string url, string text) =>
            new() { ExtractionId = id, Url = url, ObservedAtUtc = new DateTimeOffset(2026, 1, 21, 9, 0, 0, TimeSpan.Zero), Text = text };
        return new PublishedAction
        {
            Corrections = [],
            RecordId = new string('f', 64),
            RevisionNumber = 1,
            Headline = "Doe sponsors a housing bill",
            Summary = "Doe sponsored the bill.",
            TextOrigin = PublishedTextOrigin.Model,
            Congress = 119,
            BillType = "hr",
            BillNumber = 7,
            BillTitle = "A <housing> bill",
            PolicyArea = "Housing and Community Development",
            PolicyAreaCitation = Cite(detailId, detail, "Policy area: Housing and Community Development"),
            TrackedIssueIds = ["housing"],
            SummaryCitation = Cite(detailId, detail, "Summary version: 00 <i>funds</i> rent aid."),
            Sponsorships =
            [
                new PublishedSponsorship
                {
                    Official = new PublishedOfficial { Id = "o1", Name = "Jane <Doe>" },
                    Role = PublishedSponsorshipRole.Sponsor,
                    IsOriginal = false,
                    SponsoredOn = new DateOnly(2026, 1, 3),
                    Citations = [Cite(detailId, detail, "Sponsor: D000001")]
                },
                new PublishedSponsorship
                {
                    Official = new PublishedOfficial { Id = "o2", Name = "Ann Roe" },
                    Role = PublishedSponsorshipRole.Cosponsor,
                    IsOriginal = true,
                    SponsoredOn = new DateOnly(2026, 1, 5),
                    WithdrawnOn = new DateOnly(2026, 1, 20),
                    Citations = [Cite(cosponsorsId, cosponsors, "Cosponsor: R000002, original")]
                }
            ],
            Sources =
            [
                Source(detailId, "https://api.congress.gov/v3/bill/119/hr/7", detail),
                Source(cosponsorsId, "https://api.congress.gov/v3/bill/119/hr/7/cosponsors", cosponsors)
            ],
            ApprovedAtUtc = new DateTimeOffset(2026, 1, 22, 12, 0, 0, TimeSpan.Zero),
            FirstPublishedAtUtc = new DateTimeOffset(2026, 1, 23, 8, 0, 0, TimeSpan.Zero)
        };
    }

    private static PublishedVote Vote(params string[] candidateTotalLines)
    {
        var detail = "Started: 2025-04-10T11:08:00-04:00\nQuestion: On Passage\nResult: Passed <b>\n" + string.Concat(candidateTotalLines.Select(line => line + "\n"));
        const string members = "Member: A000055, Jane Doe, R, AL, Yea\n";
        var detailId = new string('1', 64);
        var membersId = new string('2', 64);
        PublishedCitation Cite(string id, string text, string quote) =>
            new() { ExtractionId = id, Start = text.IndexOf(quote, StringComparison.Ordinal), Length = quote.Length, Quote = quote };
        PublishedDocumentVersion Source(string id, string url, string text) =>
            new() { ExtractionId = id, Url = url, ObservedAtUtc = new DateTimeOffset(2026, 1, 21, 9, 0, 0, TimeSpan.Zero), Text = text };
        return new PublishedVote
        {
            Corrections = [],
            RecordId = new string('f', 64),
            RevisionNumber = 1,
            Headline = "House passes a housing bill",
            Summary = "The House voted.",
            TextOrigin = PublishedTextOrigin.Model,
            Chamber = PublishedChamber.House,
            Congress = 119,
            Session = 1,
            RollCallNumber = 100,
            Kind = PublishedVoteKind.Passage,
            KindCitation = Cite(detailId, detail, "Question: On Passage"),
            VotedOn = new DateOnly(2025, 4, 10),
            VotedOnCitation = Cite(detailId, detail, "Started: 2025-04-10T11:08:00-04:00"),
            Result = "Passed <b>",
            Totals = new() { Yea = 1, Nay = 0, Present = 0, NotVoting = 0 },
            PartyMajorities = [PublishedPartyMajority.Of("Republican", 1, 0)],
            Facts =
            [
                new PublishedVoteFact { Text = "Started: 2025-04-10T11:08:00-04:00", Citation = Cite(detailId, detail, "Started: 2025-04-10T11:08:00-04:00") },
                new PublishedVoteFact { Text = "Question: On Passage", Citation = Cite(detailId, detail, "Question: On Passage") },
                new PublishedVoteFact { Text = "Result: Passed <b>", Citation = Cite(detailId, detail, "Result: Passed <b>") },
                .. candidateTotalLines.Select(line => new PublishedVoteFact { Text = line, Citation = Cite(detailId, detail, line) })
            ],
            Members =
            [
                new PublishedMemberVote
                {
                    Official = new PublishedOfficial { Id = "o1", Name = "Jane <Doe>" },
                    Party = "Republican",
                    Cast = "Yea",
                    Citation = Cite(membersId, members, "Member: A000055, Jane Doe, R, AL, Yea")
                }
            ],
            BillType = "hr",
            BillNumber = 7,
            TrackedIssueIds = ["housing"],
            Sources =
            [
                Source(detailId, "https://api.congress.gov/v3/house-vote/119/1/100", detail),
                Source(membersId, "https://api.congress.gov/v3/house-vote/119/1/100/members", members)
            ],
            ApprovedAtUtc = new DateTimeOffset(2026, 1, 22, 12, 0, 0, TimeSpan.Zero),
            FirstPublishedAtUtc = new DateTimeOffset(2026, 1, 23, 8, 0, 0, TimeSpan.Zero)
        };
    }

    private static PublishedStatement Statement()
    {
        const string text = "Jane said <b>a rent cap</b> helps tenants. Signed 2026-05-01. <script>x</script>";
        var cap = text.IndexOf("<b>a rent cap</b>", StringComparison.Ordinal);
        var signed = text.IndexOf("Signed 2026-05-01.", StringComparison.Ordinal);
        return new PublishedStatement
        {
            Corrections = [],
            RecordId = new string('e', 64),
            RevisionNumber = 1,
            Headline = "Doe backs a rent cap",
            Summary = "Doe supports a cap.",
            StatementDate = new DateOnly(2026, 5, 1),
            StatementDateEvidence = new PublishedCitation { ExtractionId = AfterId, Start = signed, Length = 18, Quote = "Signed 2026-05-01." },
            Speakers = [new PublishedOfficial { Id = "o1", Name = "Jane <Doe>" }],
            OtherSpeakers = ["Aide One"],
            IssueIds = ["housing", "transit"],
            PolicyAreaIds = [],
            BillNumbers = ["HB 12"],
            Quotations =
            [
                new PublishedQuotation { ExtractionId = AfterId, Start = cap, Length = 17, Quote = "<b>a rent cap</b>", Kind = PublishedQuotationKind.DirectQuotation },
                new PublishedQuotation { ExtractionId = AfterId, Start = signed, Length = 18, Quote = "Signed 2026-05-01.", Kind = PublishedQuotationKind.ReleaseText }
            ],
            Source = new PublishedDocumentVersion
            {
                ExtractionId = AfterId,
                Url = "https://example.gov/story",
                ObservedAtUtc = new DateTimeOffset(2026, 5, 3, 10, 45, 0, TimeSpan.Zero),
                Text = text
            },
            ApprovedAtUtc = new DateTimeOffset(2026, 5, 10, 12, 0, 0, TimeSpan.Zero),
            FirstPublishedAtUtc = new DateTimeOffset(2026, 5, 11, 8, 0, 0, TimeSpan.Zero)
        };
    }

    private static PublishedDocumentChange Record(string beforeText = BeforeText, string afterText = AfterText)
    {
        var oldRule = beforeText.IndexOf("<b>rule</b>", StringComparison.Ordinal);
        var newRule = afterText.IndexOf("rule & scope", StringComparison.Ordinal);
        return new PublishedDocumentChange
        {
            Corrections = [],
            RecordId = new string('c', 32),
            RevisionNumber = 2,
            Headline = "Rule <script>alert(1)</script> changed",
            Summary = "The rule changed.",
            Significance = "It sets scope.",
            Limits = "Dates are observation dates.",
            Institution = "Senate",
            ChangeDate = new DateOnly(2026, 3, 1),
            ChangeDateEvidence = new PublishedCitation
            {
                ExtractionId = AfterId,
                Start = afterText.IndexOf("Keep this.", StringComparison.Ordinal),
                Length = "Keep this.".Length,
                Quote = "Keep this."
            },
            Officials = [new PublishedOfficial { Id = "o1", Name = "Jane <Doe>" }],
            IssueIds = ["housing"],
            Before = new PublishedDocumentVersion
            {
                ExtractionId = BeforeId,
                Url = "https://example.gov/before",
                ObservedAtUtc = new DateTimeOffset(2026, 4, 2, 9, 30, 0, TimeSpan.Zero),
                Text = beforeText
            },
            After = new PublishedDocumentVersion
            {
                ExtractionId = AfterId,
                Url = "https://example.gov/after",
                ObservedAtUtc = new DateTimeOffset(2026, 5, 3, 10, 45, 0, TimeSpan.Zero),
                Text = afterText
            },
            Changes =
            [
                new PublishedChange
                {
                    BeforeStart = 0, BeforeLength = beforeText.Length - 11,
                    AfterStart = 0, AfterLength = afterText.Length - 11,
                    BeforeContextStart = beforeText.Length - 10, BeforeContextLength = 10,
                    AfterContextStart = afterText.Length - 10, AfterContextLength = 10,
                    WordEdits = []
                }
            ],
            Citations =
            [
                new PublishedCitation { ExtractionId = BeforeId, Start = oldRule, Length = 11, Quote = "<b>rule</b>" },
                new PublishedCitation { ExtractionId = AfterId, Start = newRule, Length = 12, Quote = "rule & scope" }
            ],
            ApprovedAtUtc = new DateTimeOffset(2026, 5, 10, 12, 0, 0, TimeSpan.Zero),
            FirstPublishedAtUtc = new DateTimeOffset(2026, 5, 11, 8, 0, 0, TimeSpan.Zero)
        };
    }
}
