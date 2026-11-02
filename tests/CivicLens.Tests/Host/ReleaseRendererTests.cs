using CivicLens.Core.ExecutiveActions;
using CivicLens.Application.Issues;
using System.Text.RegularExpressions;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;
using CivicLens.Application.Publication;
using CivicLens.PublicApi;
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
        Assert.Contains("1 Mar 2026", withDate, StringComparison.Ordinal);
        Assert.Contains("cite-change-date", withDate, StringComparison.Ordinal);
        Assert.DoesNotContain("Actual change date", withoutDate, StringComparison.Ordinal);
        foreach (var html in new[] { withDate, withoutDate })
        {
            Assert.Contains("Earlier version 2 Apr 2026, 09:30 UTC", html, StringComparison.Ordinal);
            Assert.Contains("later version 3 May 2026, 10:45 UTC", html, StringComparison.Ordinal);
            Assert.Contains("Approved 10 May 2026", html, StringComparison.Ordinal);
            Assert.Contains("first published 11 May 2026", html, StringComparison.Ordinal);
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
        Assert.Equal(["", "documentChange", "statement", "action", "brief", "vote", "relationship", "executiveAction"],
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
    public async Task SearchPageTellsTheReaderWhatToTypeAndShowsTheJavaScriptNoticeBeforeTheFormBecauseTheFormIsHiddenWithoutScript()
    {
        var html = await new ReleaseRenderer("https://api.example.test", null).RenderSearchAsync(ReleaseWithFilters(), SearchFacets(), CancellationToken.None);
        var document = new HtmlParser().ParseDocument(html);

        Assert.Equal("Type a topic or a bill number, or choose an official below.", document.QuerySelector("form [data-search-hint]")!.TextContent);
        Assert.True(document.QuerySelector("form [data-search-none]")!.HasAttribute("hidden"));
        Assert.Equal(2, document.QuerySelectorAll("form [data-search-none] a").Length);
        var noscriptStart = html.IndexOf("<noscript>", StringComparison.Ordinal);
        var noscriptEnd = html.IndexOf("</noscript>", StringComparison.Ordinal);
        var notice = html.IndexOf("Search needs JavaScript", StringComparison.Ordinal);
        Assert.True(noscriptStart >= 0);
        Assert.InRange(notice, noscriptStart, noscriptEnd);
        Assert.True(noscriptEnd < html.IndexOf("<form", StringComparison.Ordinal));
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

        var document = new HtmlParser().ParseDocument(html);
        var text = document.QuerySelector("main")!.ShownText();
        Assert.Contains("Amendment, Suspension of the rules, Passage, Conference report, Veto override, Nomination.", text, StringComparison.Ordinal);
        Assert.Contains("A vote is substantive when its kind is one of these:", text, StringComparison.Ordinal);
        Assert.Contains("Votes they cast: the substantive roll call votes on the issue", text, StringComparison.Ordinal);
        Assert.Contains("Their statements: ", text, StringComparison.Ordinal);
        Assert.Contains("Bills they sponsored: ", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Too few records", text, StringComparison.Ordinal);
        Assert.Contains("A missed vote means the official did not vote on it. The count includes procedural votes. It counts only the roll calls on this site, which can be fewer than the chamber held.", text, StringComparison.Ordinal);
        Assert.DoesNotContain("all roll calls of the current Congress", text, StringComparison.Ordinal);
        Assert.Contains("A roll call that is not on this site is in neither number.", text, StringComparison.Ordinal);
        Assert.Contains("A split vote is a substantive roll call vote where most Democrats and most Republicans who cast Yea or Nay voted on opposite sides.", text, StringComparison.Ordinal);
        Assert.Contains($"on at least {TalkAndActionProfile.MinimumRecordsForShare} split votes", text, StringComparison.Ordinal);
        Assert.Contains("An independent who caucuses with the Democrats or the Republicans, which means they work with that party in Congress, counts with that party.", text, StringComparison.Ordinal);
        Assert.Contains("The page leaves out votes that the official cast while in another party and not caucusing with either.", text, StringComparison.Ordinal);
        Assert.Contains("For a member who has left Congress, the page uses the party letter in the vote record, so the page does not count the votes of an independent who left.", text, StringComparison.Ordinal);
        Assert.Contains("gives the possible issues. An AI model chooses the issues of a bill or vote. It reads the title, the policy area, and the subjects that Congress.gov lists for the bill, when there are any. A bill with no fitting issue shows no issue. Subjects cover only some bills. No person checks that pick. Older records list every possible issue", text, StringComparison.Ordinal);
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
    public async Task EveryPublicPageShowsDatesInTheOneDayMonthYearForm()
    {
        string[] pagesWithADate = ["record", "statement", "action", "vote", "relationship", "brief", "withdrawal", "home", "all records", "briefs", "official", "search", "executive action"];
        var pages = (await RenderEveryPageTypeAsync()).Select(page => (page.Name, page.Html))
            .Append(("executive action", await new ReleaseRenderer(null, null).RenderExecutiveActionAsync(FullExecutiveAction(), CancellationToken.None))).ToArray();
        var other = new[]
        {
            @"\d{4}-\d{2}-\d{2}", @"\b(Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec)[a-z]* \d{1,2}, \d{4}", @"\b\d{1,2}/\d{1,2}/\d{2,4}\b",
            @"\b\d{1,2} (Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec), \d{1,2}:\d{2}"
        };
        foreach (var (name, html) in pages)
        {
            var document = new HtmlParser().ParseDocument(html);
            foreach (var verbatim in document.QuerySelectorAll("script, style, template, .doc-text, blockquote").ToList()) verbatim.Remove();
            foreach (var evidence in document.QuerySelectorAll("li").Where(item => item.TextContent.StartsWith("Evidence for", StringComparison.Ordinal)).ToList()) evidence.Remove();
            var text = document.Body!.TextContent;

            foreach (var pattern in other) Assert.False(Regex.IsMatch(text, pattern), $"{name} shows a date that is not in the form 27 Mar 2026: {Regex.Match(text, pattern).Value}");
            if (pagesWithADate.Contains(name))
                Assert.True(Regex.IsMatch(text, @"\b\d{1,2} (Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec) \d{4}\b"), $"{name} shows no date");
        }
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
            await renderer.RenderActionAsync(action, null, [actionLink], PublicationCatalogs.None, CancellationToken.None),
            await renderer.RenderVoteAsync(vote, null, [voteLink], PublicationCatalogs.None, CancellationToken.None)
        };

        foreach (var html in pages)
        {
            var names = new HtmlParser().ParseDocument(html).QuerySelectorAll("main a")
                .Where(link => link.TextContent.StartsWith("Show ", StringComparison.Ordinal) || link.TextContent.StartsWith("Source line", StringComparison.Ordinal) || link.TextContent.StartsWith("Why they are linked", StringComparison.Ordinal))
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
            .ToDictionary(pair => pair.First.TextContent, pair => pair.Second.ShownText());
        Assert.Equal("Jane <Doe>", facts["Speakers"]);
        Assert.Equal("Aide One", facts["Other people quoted, named as the article writes them"]);
        Assert.Equal("1 May 2026", facts["Statement date"]);
        Assert.Equal("housing, transit", facts["Issues"]);
        Assert.False(facts.ContainsKey("Policy areas"));
        Assert.Equal("HB 12", facts["Bill numbers"]);
        Assert.Equal(["Inside quotation marks", "Not in quotation marks"],
            document.QuerySelectorAll("[aria-labelledby='quotations-title'] .badge").Select(badge => badge.TextContent));
        Assert.Equal("<b>a rent cap</b>", document.QuerySelector("mark#cite-1")!.TextContent);
        Assert.Equal("Signed 2026-05-01.", document.QuerySelector("mark#cite-2")!.TextContent);
        Assert.Equal("Signed 2026-05-01.", document.QuerySelector("mark#cite-statement-date")!.TextContent);
        Assert.Contains("Evidence for the statement date", html, StringComparison.Ordinal);
        Assert.Contains("href=\"https://example.gov/story\"", html, StringComparison.Ordinal);
        Assert.Contains("Observed 3 May 2026, 10:45 UTC", html, StringComparison.Ordinal);
        Assert.Contains("It does not show when the statement was first made.", html, StringComparison.Ordinal);
        Assert.DoesNotContain("when or why the source changed", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<script>", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StatementPageLinksTheArticleAndSaysThatTheSourceIsTheFeedWhenTheStatementCameFromAFeed()
    {
        var statement = Statement() with { ArticleUrl = "https://senator.example/news/a" };

        var html = await new ReleaseRenderer(null, null).RenderStatementAsync(statement, [], PublicationCatalogs.None, CancellationToken.None);

        var panel = new HtmlParser().ParseDocument(html).QuerySelector("[aria-labelledby='source-title']")!;
        var link = Assert.Single(panel.QuerySelectorAll("a"), anchor => anchor.GetAttribute("href") == "https://senator.example/news/a");
        Assert.Contains("nofollow", link.GetAttribute("rel"), StringComparison.Ordinal);
        Assert.Contains("feed that lists the article", panel.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ACodeApprovedAiStatementIsLabelledAnAiSummaryCheckedByCodeBecauseReadersMustNotThinkAPersonApprovedIt()
    {
        var statement = Statement() with { TextOrigin = PublishedTextOrigin.Model, ApprovedBy = PublishedApprover.Code, PolicyAreaIds = ["land-use"] };

        var html = await new ReleaseRenderer(null, null).RenderStatementAsync(statement, [], PublicationCatalogs.None, CancellationToken.None);

        var text = new HtmlParser().ParseDocument(html).QuerySelector("main")!.TextContent;
        Assert.Contains(PublishedRecordLabels.StatementModelWording, text, StringComparison.Ordinal);
        Assert.Contains("Checked by code on 10 May 2026, 12:00 UTC", text, StringComparison.Ordinal);
        Assert.Contains("Checked by code, 10 May 2026, 12:00 UTC", text, StringComparison.Ordinal);
        Assert.Contains("Policy areas (chosen by AI)", text, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"Approved \d", text);
    }

    [Fact]
    public async Task APersonApprovedStatementKeepsItsApprovedWordingAndShowsNoAiLabel()
    {
        var html = await new ReleaseRenderer(null, null).RenderStatementAsync(Statement(), [], PublicationCatalogs.None, CancellationToken.None);

        var text = new HtmlParser().ParseDocument(html).QuerySelector("main")!.TextContent;
        Assert.Contains("Approved 10 May 2026, 12:00 UTC", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Checked by code", text, StringComparison.Ordinal);
        Assert.DoesNotContain("AI summary", text, StringComparison.Ordinal);
        Assert.DoesNotContain("chosen by AI", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ACodeApprovedStatementWithADateSaysCodeReadItBecauseAReaderMustKnowNoPersonConfirmedTheDate()
    {
        var statement = Statement() with { TextOrigin = PublishedTextOrigin.Model, ApprovedBy = PublishedApprover.Code };

        var html = await new ReleaseRenderer(null, null).RenderStatementAsync(statement, [], PublicationCatalogs.None, CancellationToken.None);

        var document = new HtmlParser().ParseDocument(html);
        var facts = document.QuerySelectorAll(".facts dt").Zip(document.QuerySelectorAll(".facts dd"))
            .ToDictionary(pair => pair.First.TextContent, pair => pair.Second.ShownText());
        Assert.Equal("1 May 2026. " + PublishedRecordLabels.CodeReadDate + ".", facts["Statement date"]);
    }

    [Fact]
    public async Task APersonApprovedStatementShowsItsDateWithoutTheCodeReadLabel()
    {
        var html = await new ReleaseRenderer(null, null).RenderStatementAsync(Statement(), [], PublicationCatalogs.None, CancellationToken.None);

        Assert.DoesNotContain(PublishedRecordLabels.CodeReadDate, html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnAiStatementThatAPersonApprovedShowsApprovedAndNoAiLabelBecauseAPersonApprovedIt()
    {
        var statement = Statement() with { TextOrigin = PublishedTextOrigin.Model, ApprovedBy = PublishedApprover.Person };

        var html = await new ReleaseRenderer(null, null).RenderStatementAsync(statement, [], PublicationCatalogs.None, CancellationToken.None);

        var text = new HtmlParser().ParseDocument(html).QuerySelector("main")!.TextContent;
        Assert.Contains("Approved 10 May 2026, 12:00 UTC", text, StringComparison.Ordinal);
        Assert.DoesNotContain("AI summary", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StatementPageShowsNoArticleLineWhenTheSourceIsTheArticleItself()
    {
        var html = await new ReleaseRenderer(null, null).RenderStatementAsync(Statement(), [], PublicationCatalogs.None, CancellationToken.None);

        Assert.DoesNotContain("feed that lists the article", html, StringComparison.Ordinal);
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
    public async Task ActionPageStatesTheLatestActionWithItsDateAndReadTimeAndLinksTheBillOnCongressGovSoTheReaderCanCheckIt()
    {
        var status = new PublishedBillStatus { RecordId = Action().RecordId, Text = "Passed House.", Date = new DateOnly(2026, 1, 28), ObservedAtUtc = new DateTimeOffset(2026, 1, 30, 9, 0, 0, TimeSpan.Zero) };

        var html = await new ReleaseRenderer(null, null).RenderActionAsync(Action(), status, [], PublicationCatalogs.None, CancellationToken.None);

        var facts = new HtmlParser().ParseDocument(html).QuerySelectorAll(".facts dd").Select(dd => dd.TextContent).ToList();
        Assert.Contains("Latest official action: Passed House. Action date: 28 Jan 2026.", facts);
        Assert.Contains("30 Jan 2026, 09:00 UTC. The bill can change after this time.", facts);
        var link = new HtmlParser().ParseDocument(html).QuerySelectorAll(".facts a").Single(anchor => anchor.TextContent == "Bill on Congress.gov");
        Assert.StartsWith("https://www.congress.gov/bill/119th-congress/", link.GetAttribute("href"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ActionPageShowsThePlainMeaningOfARecognizedLatestActionWithItsDateAndNotTheRawText()
    {
        var status = new PublishedBillStatus { RecordId = Action().RecordId, Text = "Became Public Law No: 119-21.", Date = new DateOnly(2026, 1, 28), ObservedAtUtc = new DateTimeOffset(2026, 1, 30, 9, 0, 0, TimeSpan.Zero) };

        var html = await new ReleaseRenderer(null, null).RenderActionAsync(Action(), status, [], PublicationCatalogs.None, CancellationToken.None);

        var facts = new HtmlParser().ParseDocument(html).QuerySelectorAll(".facts dd").Select(dd => dd.TextContent).ToList();
        Assert.Contains("Became law: Public Law 119-21. Action date: 28 Jan 2026.", facts);
        Assert.DoesNotContain("Became Public Law No", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ActionPageWithNoStoredStatusShowsNoStatusLineSoItNeverClaimsThatNothingHappened()
    {
        var html = await new ReleaseRenderer(null, null).RenderActionAsync(Action(), null, [], PublicationCatalogs.None, CancellationToken.None);

        Assert.DoesNotContain("Latest action", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Status read on", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ActionPageLinksTheSponsorButNotACosponsorBecauseOnlyTheSponsorOwnsTheAction()
    {
        var html = await new ReleaseRenderer(null, null).RenderActionAsync(Action(), null, [], PublicationCatalogs.None, CancellationToken.None);

        var rows = new HtmlParser().ParseDocument(html).QuerySelectorAll("[aria-labelledby='sponsorships-title'] tbody tr").ToList();
        Assert.Equal("../officials/o1.html", rows.Single(row => row.QuerySelector("td")!.TextContent == "Jane <Doe>")
            .QuerySelector("td a")!.GetAttribute("href"));
        Assert.Null(rows.Single(row => row.QuerySelector("td")!.TextContent == "Ann Roe").QuerySelector("td a[href^='../officials/']"));
    }

    [Fact]
    public async Task VotePageLinksEachTrackedMemberToTheOfficialPage()
    {
        var html = await new ReleaseRenderer(null, null).RenderVoteAsync(Vote(), null, [], PublicationCatalogs.None, CancellationToken.None);

        var link = new HtmlParser().ParseDocument(html).QuerySelector("[aria-labelledby='members-title'] li a")!;
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
        var html = await new ReleaseRenderer(null, null).RenderActionAsync(Action(), null, [], HousingCatalog, CancellationToken.None);

        var document = new HtmlParser().ParseDocument(html);
        var facts = document.QuerySelectorAll(".facts dt").Zip(document.QuerySelectorAll(".facts dd"))
            .ToDictionary(pair => pair.First.TextContent, pair => pair.Second.ShownText());
        Assert.Equal("HR 7, Congress 119", facts["Bill"]);
        Assert.Equal("A <housing> bill", facts["Title"]);
        Assert.Equal("Housing and Community Development", facts["Policy area"]);
        Assert.Equal("Housing costs", facts["Tracked issues"]);
        var rows = document.QuerySelectorAll("[aria-labelledby='sponsorships-title'] tbody tr")
            .Select(row => string.Join("|", row.QuerySelectorAll("td").Select(cell => cell.ShownText().Trim())));
        Assert.Equal(["Jane <Doe>|Sponsor|No|3 Jan 2026|None|", "Ann Roe|Cosponsor|Yes|5 Jan 2026|20 Jan 2026|"], rows);
        Assert.Equal(["Source line: Jane <Doe>, sponsorship", "Source line: Ann Roe, sponsorship"],
            document.QuerySelectorAll("[aria-labelledby='sponsorships-title'] a.source-line").Select(link => link.TextContent));
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

        foreach (var html in new[] { await renderer.RenderActionAsync(Action(), null, [], PublicationCatalogs.None, CancellationToken.None),
                     await renderer.RenderVoteAsync(Vote(), null, [], PublicationCatalogs.None, CancellationToken.None) })
        {
            Assert.Contains("From the official record, checked", html, StringComparison.Ordinal);
            Assert.DoesNotMatch(@"Approved \d", html);
        }
    }

    [Fact]
    public async Task ActionAndVotePagesLabelAnAiSummaryOnlyWhenAModelWroteTheText()
    {
        var renderer = new ReleaseRenderer(null, null);
        var action = Action();
        var vote = Vote();
        var person = PublishedTextOrigin.Person;

        var model = new[] { await renderer.RenderActionAsync(action, null, [], PublicationCatalogs.None, CancellationToken.None),
            await renderer.RenderVoteAsync(vote, null, [], PublicationCatalogs.None, CancellationToken.None) };
        var written = new[] { await renderer.RenderActionAsync(action with { TextOrigin = person }, null, [], PublicationCatalogs.None, CancellationToken.None),
            await renderer.RenderVoteAsync(vote with { TextOrigin = person }, null, [], PublicationCatalogs.None, CancellationToken.None) };

        Assert.All(model, html => Assert.Contains("An AI summary of the official record", html, StringComparison.Ordinal));
        Assert.All(written, html => Assert.DoesNotContain("AI summary", html, StringComparison.Ordinal));
    }

    [Fact]
    public async Task AnActionPageLabelsCodeWrittenWordingWithItsSourceAndNeverCallsItAnAiSummary()
    {
        var html = await new ReleaseRenderer(null, null).RenderActionAsync(Action() with { TextOrigin = PublishedTextOrigin.Code }, null, [],
            PublicationCatalogs.None, CancellationToken.None);

        Assert.Contains("Wording", html, StringComparison.Ordinal);
        Assert.Contains("Written automatically from Congress.gov data, with no AI", html, StringComparison.Ordinal);
        Assert.DoesNotContain("AI summary", html, StringComparison.Ordinal);
        Assert.Contains("Chosen by an AI model from the bill title and subjects", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnActionPageSaysAModelChoseTheIssuesOnlyBesideCodeWrittenWordingAndOnlyWhenTheRecordHasIssues()
    {
        var codeWithoutIssues = await new ReleaseRenderer(null, null).RenderActionAsync(
            Action() with { TextOrigin = PublishedTextOrigin.Code, TrackedIssueIds = [] }, null, [], PublicationCatalogs.None, CancellationToken.None);
        var modelText = await new ReleaseRenderer(null, null).RenderActionAsync(Action(), null, [], PublicationCatalogs.None, CancellationToken.None);

        Assert.DoesNotContain(PublishedRecordLabels.ModelChosenIssues, codeWithoutIssues, StringComparison.Ordinal);
        Assert.DoesNotContain(PublishedRecordLabels.ModelChosenIssues, modelText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EveryActionQuoteLinksToTheMarkOfTheSameTextInTheSourceAndEachSourceIsLabelledByPosition()
    {
        var html = await new ReleaseRenderer(null, null).RenderActionAsync(Action(), null, [], PublicationCatalogs.None, CancellationToken.None);

        var document = new HtmlParser().ParseDocument(html);
        var links = document.QuerySelectorAll("a[href^='#cite-']").ToArray();
        Assert.Equal(4, links.Length);
        Assert.Equal(Action().Citations().Select(citation => citation.Quote).Order(StringComparer.Ordinal),
            links.Select(link => document.QuerySelector(link.GetAttribute("href")!)!.TextContent).Order(StringComparer.Ordinal));
        Assert.Equal(["Source text 1 of 2", "Source text 2 of 2"],
            document.QuerySelectorAll("[aria-labelledby='sources-title'] [aria-label]").Select(item => item.GetAttribute("aria-label")));
    }

    [Fact]
    public async Task EachActionFactRowThatTheSourceStatesLinksToItsLineAndTheOthersHaveNoLink()
    {
        var lines = LinkedLines(await new ReleaseRenderer(null, null).RenderActionAsync(Action(), null, [], HousingCatalog, CancellationToken.None));

        Assert.Equal("Policy area: Housing and Community Development", lines["Policy area"]);
        Assert.Null(lines["Bill"]);
        Assert.Null(lines["Title"]);
    }

    [Fact]
    public async Task TwoSourceLinesOfOneSponsorshipAreNamedByNumberSoTheyAreNotTheSameLink()
    {
        var action = Action();
        var first = action.Sponsorships[0] with { Citations = [action.Sponsorships[0].Citations[0], action.SummaryCitation!] };
        var html = await new ReleaseRenderer(null, null).RenderActionAsync(action with { Sponsorships = [first, action.Sponsorships[1]] }, null, [], PublicationCatalogs.None, CancellationToken.None);

        var row = new HtmlParser().ParseDocument(html).QuerySelector("[aria-labelledby='sponsorships-title'] tbody tr")!;
        Assert.Equal(["Source line: Jane <Doe>, sponsorship, line 1", "Source line: Jane <Doe>, sponsorship, line 2"],
            row.QuerySelectorAll("a.source-line").Select(link => link.TextContent));
    }

    [Fact]
    public async Task ActionPageRejectsACitationThatNamesNoEmbeddedSource()
    {
        var action = Action() with { SummaryCitation = Action().SummaryCitation! with { ExtractionId = new string('9', 64) } };

        await Assert.ThrowsAsync<InvalidDataException>(() => new ReleaseRenderer(null, null).RenderActionAsync(action, null, [], PublicationCatalogs.None, CancellationToken.None));
    }

    [Fact]
    public async Task VotePageShowsTheDayTheKindTheResultFactsTheCastOfEachTrackedMemberAndTheSources()
    {
        var html = await new ReleaseRenderer(null, null).RenderVoteAsync(Vote(), null, [], HousingCatalog, CancellationToken.None);

        var document = new HtmlParser().ParseDocument(html);
        var facts = document.QuerySelectorAll(".facts dt").Zip(document.QuerySelectorAll(".facts dd"))
            .ToDictionary(pair => pair.First.TextContent, pair => pair.Second.ShownText());
        Assert.Equal("10 Apr 2025", facts["Voted on"]);
        Assert.Equal("Passage", facts["Kind"]);
        Assert.Equal("On Passage", facts["Question"]);
        Assert.Equal("House", facts["Chamber"]);
        Assert.Equal("100, Congress 119, session 1", facts["Roll call"]);
        Assert.Equal("Housing costs", facts["Tracked issues"]);
        Assert.Equal("Passed <b>", facts["Result"]);
        Assert.Null(document.QuerySelector("[aria-labelledby='result-title']"));
        Assert.Empty(document.QuerySelectorAll("blockquote"));
        var members = document.QuerySelector("[aria-labelledby='members-title']")!;
        Assert.Equal(["Voted Yea (1 tracked House member)"], members.QuerySelectorAll("h3").Select(heading => heading.ShownText()));
        Assert.Equal(["Jane <Doe>"], members.QuerySelectorAll("li a:first-child").Select(link => link.TextContent));
        Assert.Empty(members.QuerySelectorAll("blockquote"));
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

        Assert.Equal("1", facts["Votes for Emmer, all House members"]);
        Assert.Equal("218", facts["Votes for Johnson (LA), all House members"]);
        Assert.DoesNotContain("Totals", facts.Keys);
        Assert.Contains("Votes for Emmer, all senators", (await VoteFactsOf(vote with { Chamber = PublishedChamber.Senate })).Keys);
    }

    [Theory]
    [InlineData("Present", "Present, all House members")]
    [InlineData("Not Voting", "Not voting, all House members")]
    public async Task ACandidateTotalThatIsNotACandidateReadsAsACastAndNotAsVotesForIt(string name, string label)
    {
        var vote = Vote($"Candidate total {name}: 3") with { Totals = null, PartyMajorities = [] };

        Assert.Equal("3", (await VoteFactsOf(vote))[label]);
    }

    private static async Task<Dictionary<string, string>> VoteFactsOf(PublishedVote vote)
    {
        var html = await new ReleaseRenderer(null, null).RenderVoteAsync(vote, null, [], PublicationCatalogs.None, CancellationToken.None);
        var document = new HtmlParser().ParseDocument(html);
        return document.QuerySelectorAll(".facts dt").Zip(document.QuerySelectorAll(".facts dd"))
            .ToDictionary(pair => pair.First.TextContent, pair => pair.Second.ShownText());
    }

    [Theory]
    [InlineData(PublishedChamber.House, PublishedVoteSource.CongressGov, "Congress.gov House vote data", "https://clerk.house.gov/Votes/2025100")]
    [InlineData(PublishedChamber.Senate, PublishedVoteSource.Voteview, "Voteview, an academic database at UCLA (voteview.com)", "https://www.senate.gov/legislative/LIS/roll_call_votes/vote1191/vote_119_1_00100.htm")]
    public async Task TheVotePageNamesItsDataSourceAndLinksTheOfficialRollCallPageSoAReaderCanCheckItAtTheChamber(
        PublishedChamber chamber, PublishedVoteSource source, string expectedSource, string expectedLink)
    {
        var html = await new ReleaseRenderer(null, null).RenderVoteAsync(Vote() with { Chamber = chamber, Source = source }, null, [], PublicationCatalogs.None, CancellationToken.None);

        var document = new HtmlParser().ParseDocument(html);
        var facts = document.QuerySelectorAll(".facts dt").Zip(document.QuerySelectorAll(".facts dd"))
            .ToDictionary(pair => pair.First.TextContent, pair => pair.Second);
        Assert.Contains(expectedSource, facts["Data source"].TextContent, StringComparison.Ordinal);
        Assert.Equal(expectedLink, facts["Official roll call page"].QuerySelector("a")!.GetAttribute("href"));
        Assert.Contains(expectedSource.Split(' ')[0].TrimEnd(','), document.QuerySelector("[aria-labelledby='sources-title'] p")!.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AVoteviewVoteSaysThatVoteviewListsNoVicePresidentTieBreakVoteSoAFiftyFiftyResultIsNotMisread()
    {
        var html = await new ReleaseRenderer(null, null).RenderVoteAsync(Vote() with { Chamber = PublishedChamber.Senate, Source = PublishedVoteSource.Voteview }, null, [],
            PublicationCatalogs.None, CancellationToken.None);

        var caveat = new HtmlParser().ParseDocument(html).QuerySelector(".caveat")!.TextContent;
        Assert.Contains("does not list a Vice President tie-break vote", caveat, StringComparison.Ordinal);
        Assert.Contains("Civic Lens shows that code as a word such as Yea or Nay. The Senate roll call may use other words", caveat, StringComparison.Ordinal);
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
            .ToDictionary(pair => pair.First.TextContent, pair => pair.Second.ShownText());
        Assert.Equal(label, facts["Kind"]);
    }

    // The saved line says "Party total D". The label above the exact quote must say which party that is.
    [Fact]
    public async Task TheVoteFactsNameEachPartyTotalInWordsAndKeepTheSavedValueExact()
    {
        var totals = new[] { "Party total D: Yea 0, Nay 1", "Party total R: Yea 2, Nay 0", "Party total I: Yea 0, Nay 0", "Party total X: Yea 0, Nay 0" };
        var facts = await VoteFactsOf(Vote(totals));
        Assert.Equal(["Democratic party total", "Republican party total", "Independent members' total", "Party total X"],
            facts.Keys.Where(label => label.Contains("total", StringComparison.OrdinalIgnoreCase) && label != "Totals"));
        string[] labels = ["Democratic party total", "Republican party total", "Independent members' total", "Party total X"];
        Assert.Equal(["Yea 0, Nay 1", "Yea 2, Nay 0", "Yea 0, Nay 0", "Yea 0, Nay 0"], labels.Select(label => facts[label]));
    }

    [Fact]
    public async Task EachVoteFactRowLinksToItsOwnLineInTheSourceAndRowsThatAreNotInTheSourceHaveNoLink()
    {
        var lines = await LinkedLinesOf(Vote("Party total D: Yea 0, Nay 1"));

        Assert.Equal("Started: 2025-04-10T11:08:00-04:00", lines["Voted on"]);
        Assert.Equal("Question: On Passage", lines["Question"]);
        Assert.Equal("Result: Passed <b>", lines["Result"]);
        Assert.Equal("Party total D: Yea 0, Nay 1", lines["Democratic party total"]);
        Assert.Null(lines["Kind"]);
        Assert.Null(lines["Chamber"]);
        Assert.Null(lines["Roll call"]);
        Assert.Null(lines["Totals"]);
    }

    [Fact]
    public async Task TheCountLineOfAVoteIsCitedOnTheTotalsRowAndNotShownAsASecondRow()
    {
        var count = "Count: Yea 1, Nay 0, Present 0, Not Voting 0";
        var vote = Vote(count);

        var facts = await VoteFactsOf(vote);
        var lines = await LinkedLinesOf(vote);

        Assert.Equal(count, lines["Totals"]);
        Assert.DoesNotContain("Count", facts.Keys);
        Assert.Single(facts.Keys, label => label == "Totals");
    }

    private static async Task<Dictionary<string, string?>> LinkedLinesOf(PublishedVote vote) =>
        LinkedLines(await new ReleaseRenderer(null, null).RenderVoteAsync(vote, null, [], PublicationCatalogs.None, CancellationToken.None));

    private static Dictionary<string, string?> LinkedLines(string html)
    {
        var document = new HtmlParser().ParseDocument(html);
        return document.QuerySelectorAll(".facts dt").Zip(document.QuerySelectorAll(".facts dd"))
            .ToDictionary(pair => pair.First.TextContent,
                pair => pair.Second.QuerySelector("a.source-line") is { } link ? document.QuerySelector(link.GetAttribute("href")!)!.TextContent : null);
    }

    [Fact]
    public async Task AnOtherKindVoteWithAQuestionQuoteIsCalledAnOtherQuestionAndShowsTheQuestion()
    {
        var html = await new ReleaseRenderer(null, null).RenderVoteAsync(Vote() with { Kind = PublishedVoteKind.Other }, null, [], PublicationCatalogs.None, CancellationToken.None);

        var facts = Facts(html);
        Assert.Equal("Other question", facts["Kind"]);
        Assert.Equal("On Passage", facts["Question"]);
    }

    [Fact]
    public async Task AnOtherKindVoteWithNoQuestionQuoteSaysTheSourceGivesNoQuestionSoTheReaderIsNotSentToANonexistentQuote()
    {
        var vote = Vote();
        vote = vote with { Kind = PublishedVoteKind.Other, KindCitation = null, Facts = [.. vote.Facts.Where(fact => !fact.Text.StartsWith("Question:", StringComparison.Ordinal))] };

        var html = await new ReleaseRenderer(null, null).RenderVoteAsync(vote, null, [], PublicationCatalogs.None, CancellationToken.None);

        var facts = Facts(html);
        Assert.Equal("Other question. The source gives no question.", facts["Kind"]);
        Assert.DoesNotContain("Question", facts.Keys);
    }

    [Fact]
    public async Task APublicPageOmitsFactRowsThatHaveNoValueInsteadOfPrintingNone()
    {
        var renderer = new ReleaseRenderer(null, null);
        var vote = Vote() with { TrackedIssueIds = [] };
        var action = Action() with { TrackedIssueIds = [], PolicyArea = null, PolicyAreaId = null, PolicyAreaCitation = null };
        var statement = Statement() with { OtherSpeakers = [], IssueIds = [], PolicyAreaIds = [], BillNumbers = [] };

        foreach (var html in new[] { await renderer.RenderVoteAsync(vote, null, [], HousingCatalog, CancellationToken.None),
                     await renderer.RenderActionAsync(action, null, [], HousingCatalog, CancellationToken.None),
                     await renderer.RenderStatementAsync(statement, [], HousingCatalog, CancellationToken.None),
                     await renderer.RenderRecordAsync(Record() with { Officials = [], IssueIds = [] }, HousingCatalog, CancellationToken.None) })
        {
            var labels = Facts(html).Keys;
            Assert.DoesNotContain("Tracked issues", labels);
            Assert.DoesNotContain("Issues", labels);
            Assert.DoesNotContain(PublishedRecordLabels.OtherPeopleQuoted, labels);
            Assert.DoesNotContain("Policy area", labels);
            Assert.DoesNotContain("Policy areas", labels);
            Assert.DoesNotContain("Bill numbers", labels);
            Assert.DoesNotContain("Officials", labels);
            Assert.DoesNotContain("None", Facts(html).Values);
        }
    }

    [Fact]
    public async Task ADateEvidenceQuoteIsOneLineWithALinkToItsMarkAndNoQuoteBox()
    {
        var renderer = new ReleaseRenderer(null, null);
        var pages = new[]
        {
            (await renderer.RenderStatementAsync(Statement(), [], PublicationCatalogs.None, CancellationToken.None), "Signed 2026-05-01."),
            (await renderer.RenderRecordAsync(Record(), PublicationCatalogs.None, CancellationToken.None), Record().ChangeDateEvidence!.Quote)
        };

        foreach (var (html, quote) in pages)
        {
            var document = new HtmlParser().ParseDocument(html);
            var line = document.QuerySelectorAll("li").Single(item => item.TextContent.StartsWith("Evidence for the", StringComparison.Ordinal));
            Assert.Contains(quote, line.TextContent, StringComparison.Ordinal);
            Assert.Empty(line.QuerySelectorAll("blockquote"));
            Assert.Equal(quote, document.QuerySelector(line.QuerySelector("a")!.GetAttribute("href")!)!.TextContent);
        }
    }

    [Fact]
    public async Task StatementQuotesAreNumberedAndSayWhetherTheyLieInsideQuotationMarks()
    {
        var html = await new ReleaseRenderer(null, null).RenderStatementAsync(Statement(), [], PublicationCatalogs.None, CancellationToken.None);

        var panel = new HtmlParser().ParseDocument(html).QuerySelector("[aria-labelledby='quotations-title']")!.TextContent;
        Assert.Contains("Quote 1", panel, StringComparison.Ordinal);
        Assert.Contains("Each quote is the exact text of the source.", panel, StringComparison.Ordinal);
        Assert.DoesNotContain("Quotation 1", panel, StringComparison.Ordinal);
        Assert.Equal(["Inside quotation marks", "Not in quotation marks"],
            new HtmlParser().ParseDocument(html).QuerySelectorAll("[aria-labelledby='quotations-title'] .badge").Select(badge => badge.TextContent));
    }

    private static Dictionary<string, string> Facts(string html)
    {
        var document = new HtmlParser().ParseDocument(html);
        return document.QuerySelectorAll(".facts dt").Zip(document.QuerySelectorAll(".facts dd"))
            .ToDictionary(pair => pair.First.TextContent, pair => pair.Second.ShownText());
    }

    [Fact]
    public async Task EveryVoteQuoteLinksToTheMarkOfTheSameTextInTheSource()
    {
        var vote = Vote();
        var html = await new ReleaseRenderer(null, null).RenderVoteAsync(vote, null, [], PublicationCatalogs.None, CancellationToken.None);

        var document = new HtmlParser().ParseDocument(html);
        var links = document.QuerySelectorAll("a[href^='#cite-']").ToArray();
        Assert.Equal(4, links.Length);
        Assert.Equal(vote.Citations().Select(citation => citation.Quote).Distinct().Order(StringComparer.Ordinal),
            links.Select(link => document.QuerySelector(link.GetAttribute("href")!)!.TextContent).Order(StringComparer.Ordinal));
    }

    // A House vote lists about 435 members. A quote for each member once made the page grow past 200 KB.
    // The budget leaves room for real names, which are longer than the test names.
    [Fact]
    public async Task AHouseVotePageWithEveryMemberStaysWithinItsSizeBudget()
    {
        // In each party every second member of the first 216 votes against the party. That gives 216 breaks.
        var voters = Enumerable.Range(0, 435).Select(index =>
        {
            var democrat = index < 218;
            var position = democrat ? index : index - 218;
            var breaks = position < 216 && position % 2 == 1;
            return ($"Member {index}", democrat ? PublishedPartyMajority.Democrat : PublishedPartyMajority.Republican, democrat ^ breaks ? "Yea" : "Nay");
        }).ToArray();
        PublishedPartyMajority CountOf(string party) => PublishedPartyMajority.Of(party, voters.Count(voter => voter.Item2 == party && voter.Item3 == "Yea"), voters.Count(voter => voter.Item2 == party && voter.Item3 == "Nay"));
        var vote = VoteWithParties([CountOf(PublishedPartyMajority.Democrat), CountOf(PublishedPartyMajority.Republican)], voters);

        var html = await new ReleaseRenderer(null, null).RenderVoteAsync(vote, null, [], PublicationCatalogs.None, CancellationToken.None);
        Assert.True(new HtmlParser().ParseDocument(html).QuerySelectorAll("[aria-labelledby='breakers-title'] li").Length > 200);

        var bytes = System.Text.Encoding.UTF8.GetByteCount(html);
        Assert.True(bytes <= 150_000, $"The page is {bytes} bytes.");
    }

    [Fact]
    public async Task TheVotePageGroupsMembersByCastWithTheCountSoAReaderSeesWhoVotedEachWay()
    {
        var vote = VoteWithCasts([("Zed", "Not Voting"), ("Pat", "Present"), ("Carl", "Yea"), ("Dee", "Emmer"), ("Eve", "Jeffries"), ("Bo", "Nay"), ("Amy", "Yea"), ("Fay", "Jeffries")]);
        var html = await new ReleaseRenderer(null, null).RenderVoteAsync(vote, null, [], PublicationCatalogs.None, CancellationToken.None);

        var members = new HtmlParser().ParseDocument(html).QuerySelector("[aria-labelledby='members-title']")!;
        Assert.Equal(["Voted Yea (2 tracked House members)", "Voted Nay (1 tracked House member)", "Voted for Jeffries (2 tracked House members)", "Voted for Emmer (1 tracked House member)", "Voted Present (1 tracked House member)", "Did not vote (1 tracked House member)"],
            members.QuerySelectorAll("h3").Select(heading => heading.ShownText()));
        Assert.Equal(["Amy", "Carl"], members.QuerySelectorAll(".member-group:first-of-type li a:first-child").Select(link => link.TextContent));
    }

    [Fact]
    public async Task ASplitVoteNamesEachPartySideAndListsOnlyTheMembersWhoBrokeWithTheirPartyByParty()
    {
        var vote = VoteWithParties([.. DemocratsYeaRepublicansNay, PublishedPartyMajority.Of("Independent", 1, 0), PublishedPartyMajority.Of(PublishedPartyMajority.UnknownParty, 0, 1)],
            ("Dana", "Democrat", "Yea"), ("Dora", "Democrat", "Nay"), ("Rex", "Republican", "Nay"), ("Ruth", "Republican", "Yea"),
            ("Ian", "Independent", "Yea"), ("Una", PublishedPartyMajority.UnknownParty, "Nay"), ("Pia", "Republican", "Present"), ("Ned", "Democrat", PublishedOfficialVote.NotVoting));

        var page = await RenderedAsync(vote);

        var parties = page.QuerySelector("[aria-labelledby='parties-title']")!;
        Assert.Equal("Split vote", parties.QuerySelector(".badge")!.TextContent);
        Assert.Contains("Split vote: most House Democrats who cast Yea or Nay voted Yea, and most House Republicans who cast Yea or Nay voted Nay.", parties.TextContent);
        Assert.Contains("House Democrats: 2 Yea, 1 Nay. House Republicans: 1 Yea, 3 Nay. These counts include every member of the party, tracked or not. An independent counts with the party they caucus with, so these counts can differ from the source totals. Aye counts as Yea and No counts as Nay.", parties.TextContent);
        var breakers = page.QuerySelector("[aria-labelledby='breakers-title']")!;
        Assert.Equal(["House Democrats: 1 tracked member voted against most of the party", "House Republicans: 1 tracked member voted against most of the party"], breakers.QuerySelectorAll("h3").Select(heading => heading.TextContent));
        Assert.Equal(["Dora", "Ruth"], breakers.QuerySelectorAll("li a:first-child").Select(link => link.TextContent));
        Assert.DoesNotContain("These counts include", breakers.TextContent);
        foreach (var name in new[] { "Ian", "Una", "Pia", "Ned" }) Assert.DoesNotContain(name, breakers.TextContent);
    }

    [Fact]
    public async Task ASplitVoteWhereNoMemberBrokeSaysSoInsteadOfShowingAnEmptyList()
    {
        var vote = VoteWithParties(DemocratsYeaRepublicansNay, ("Dana", "Democrat", "Yea"), ("Rex", "Republican", "Nay"));

        var breakers = (await RenderedAsync(vote)).QuerySelector("[aria-labelledby='breakers-title']")!;

        Assert.Empty(breakers.QuerySelectorAll("li"));
        Assert.Contains("No tracked House Democrat voted against most House Democrats.", breakers.TextContent);
        Assert.Contains("No tracked House Republican voted against most House Republicans.", breakers.TextContent);
        Assert.Empty(breakers.QuerySelectorAll("h3"));
        Assert.DoesNotContain("0 tracked", breakers.TextContent);
    }

    [Fact]
    public async Task ASenateSplitVoteNamesTheSenateInEveryPartyLine()
    {
        var vote = VoteWithParties(DemocratsYeaRepublicansNay, ("Sam", "Democrat", "Nay"), ("Rex", "Republican", "Nay")) with { Chamber = PublishedChamber.Senate };

        var page = await RenderedAsync(vote);

        var parties = page.QuerySelector("[aria-labelledby='parties-title']")!.TextContent;
        Assert.Contains("most Senate Democrats who cast Yea or Nay voted Yea, and most Senate Republicans who cast Yea or Nay voted Nay.", parties);
        Assert.Contains("Senate Democrats: 2 Yea, 1 Nay. Senate Republicans: 1 Yea, 3 Nay.", parties);
        var breakers = page.QuerySelector("[aria-labelledby='breakers-title']")!.TextContent;
        Assert.Contains("Senate Democrats: 1 tracked member voted against most of the party", breakers);
        Assert.Contains("No tracked Senate Republican voted against most Senate Republicans.", breakers);
    }

    [Fact]
    public async Task AnAyeOrNoCastBreaksFromThePartyLikeAYeaOrNayAndTheRowKeepsTheWordOfTheSource()
    {
        var vote = VoteWithParties(DemocratsYeaRepublicansNay, ("Dora", "Democrat", "No"), ("Ruth", "Republican", "Aye"), ("Dana", "Democrat", "Aye"));

        var breakers = (await RenderedAsync(vote)).QuerySelector("[aria-labelledby='breakers-title']")!;

        Assert.Equal(["Dora", "Ruth"], breakers.QuerySelectorAll("li a:first-child").Select(link => link.TextContent));
        Assert.Equal(["Dora voted No", "Ruth voted Aye"], breakers.QuerySelectorAll("li").Select(row => Regex.Replace(row.ShownText(), @"\s+", " ").Trim()));
        Assert.Empty(breakers.QuerySelectorAll("li a:not(:first-child)"));
    }

    [Fact]
    public async Task AFormerIndependentIsInNeitherPartyCountNorTheBreakList()
    {
        var vote = VoteWithParties([.. DemocratsYeaRepublicansNay, PublishedPartyMajority.Of("Independent", 0, 1)],
            ("Dora", "Democrat", "Nay"), ("Rex", "Republican", "Nay"), ("Fran", "Independent", "Nay"));

        var page = await RenderedAsync(vote);

        var parties = page.QuerySelector("[aria-labelledby='parties-title']")!.TextContent;
        Assert.Contains("House Democrats: 2 Yea, 1 Nay. House Republicans: 1 Yea, 3 Nay.", parties);
        Assert.DoesNotContain("Independent", parties);
        var breakers = page.QuerySelector("[aria-labelledby='breakers-title']")!;
        Assert.Equal(["Dora"], breakers.QuerySelectorAll("li a:first-child").Select(link => link.TextContent));
        Assert.DoesNotContain("Fran", breakers.TextContent);
    }

    [Fact]
    public async Task ASplitVoteSaysWhoCastYeaOrNayBecausePresentMembersDoNotSetTheMajority()
    {
        var vote = VoteWithParties(DemocratsYeaRepublicansNay, ("Dana", "Democrat", "Present"), ("Dora", "Democrat", "Present"), ("Rex", "Republican", "Present"));

        var page = await RenderedAsync(vote);

        Assert.Contains("who cast Yea or Nay", page.QuerySelector("[aria-labelledby='parties-title']")!.TextContent);
        Assert.Empty(page.QuerySelectorAll("[aria-labelledby='breakers-title'] li"));
        Assert.Contains("Present and Not voting never count as a break.", page.QuerySelector("[aria-labelledby='breakers-title']")!.TextContent);
    }

    [Fact]
    public async Task APairOfPartiesThatAgreeByOneMemberShowsTheCountsSoAThinMarginIsNotHidden()
    {
        var vote = VoteWithParties([PublishedPartyMajority.Of(PublishedPartyMajority.Democrat, 2, 1), PublishedPartyMajority.Of(PublishedPartyMajority.Republican, 2, 1)],
            ("Dana", "Democrat", "Nay"), ("Rex", "Republican", "Yea"));

        var page = await RenderedAsync(vote);

        var parties = page.QuerySelector("[aria-labelledby='parties-title']")!;
        Assert.Equal("Both parties agreed", parties.QuerySelector(".badge")!.TextContent);
        Assert.Contains("Both parties agreed: most House Democrats and most House Republicans who cast Yea or Nay voted Yea.", parties.TextContent);
        Assert.Contains("House Democrats: 2 Yea, 1 Nay. House Republicans: 2 Yea, 1 Nay. These counts include every member of the party, tracked or not.", parties.TextContent);
        Assert.Null(page.QuerySelector("[aria-labelledby='breakers-title']"));
    }

    [Fact]
    public async Task ASpeakerElectionGetsNoBadgeAndNoBreakPanelBecauseItHasNoYeaOrNayTotals()
    {
        var page = await RenderedAsync(Vote("Candidate total Emmer: 1", "Candidate total Johnson (LA): 218") with { Totals = null, PartyMajorities = [] });

        Assert.Null(page.QuerySelector("[aria-labelledby='parties-title']"));
        Assert.Null(page.QuerySelector("[aria-labelledby='breakers-title']"));
    }

    [Theory]
    [InlineData(PublishedVoteKind.Passage, 2, 2)]
    [InlineData(PublishedVoteKind.Cloture, 2, 1)]
    public async Task ATiedPartyAndAProceduralVoteGetNoBadgeBecauseNeitherCountsAsASplitVote(PublishedVoteKind kind, int democratYea, int democratNay)
    {
        var vote = VoteWithParties([PublishedPartyMajority.Of(PublishedPartyMajority.Democrat, democratYea, democratNay), PublishedPartyMajority.Of(PublishedPartyMajority.Republican, 1, 3)],
            ("Dana", "Democrat", "Nay"), ("Rex", "Republican", "Nay")) with
        { Kind = kind };

        var page = await RenderedAsync(vote);

        Assert.Null(page.QuerySelector("[aria-labelledby='parties-title']"));
        Assert.Null(page.QuerySelector("[aria-labelledby='breakers-title']"));
    }

    [Fact]
    public async Task EachCastWordOnTheVotePageKeepsItsTermSoAReaderCanLookUpYeaNayPresentAndNotVoting()
    {
        var vote = VoteWithCasts([("Zed", "Not Voting"), ("Pat", "Present"), ("Carl", "Yea"), ("Bo", "Nay"), ("Dee", "Emmer")]);
        var html = await new ReleaseRenderer(null, null).RenderVoteAsync(vote, null, [], PublicationCatalogs.None, CancellationToken.None);

        var members = new HtmlParser().ParseDocument(html).QuerySelector("[aria-labelledby='members-title']")!;
        var terms = members.QuerySelectorAll("h3 button.term").Select(button => (button.TextContent, button.GetAttribute("popovertarget"))).ToArray();
        Assert.Equal([("Yea", "term-yea"), ("Nay", "term-nay"), ("Present", "term-present"), ("Did not vote", "term-not-voting")], terms);
        Assert.Contains(Glossary.Term("present").Means, members.QuerySelector("#term-present")!.TextContent);
    }

    [Theory]
    [InlineData(PublishedVoteKind.Passage, "term-passage")]
    [InlineData(PublishedVoteKind.Amendment, "term-amendment")]
    [InlineData(PublishedVoteKind.Rule, "term-house-rule")]
    [InlineData(PublishedVoteKind.Suspension, "term-suspension-of-the-rules")]
    [InlineData(PublishedVoteKind.Nomination, "term-confirmation-vote")]
    [InlineData(PublishedVoteKind.MotionToProceed, "term-motion-to-proceed")]
    public async Task TheKindOfAVoteIsATermAndKeepsItsVisibleLabel(PublishedVoteKind kind, string popover)
    {
        var html = await new ReleaseRenderer(null, null).RenderVoteAsync(Vote() with { Kind = kind }, null, [], PublicationCatalogs.None, CancellationToken.None);

        var button = new HtmlParser().ParseDocument(html).QuerySelector(".facts button.term")!;
        Assert.Equal(popover, button.GetAttribute("popovertarget"));
        Assert.Equal(PublishedRecordLabels.VoteKind(kind), button.TextContent);
    }

    [Fact]
    public async Task AnOtherKindVoteHasNoKindTermBecauseTheGlossaryHasNoDefinitionForIt()
    {
        var html = await new ReleaseRenderer(null, null).RenderVoteAsync(Vote() with { Kind = PublishedVoteKind.Other }, null, [], PublicationCatalogs.None, CancellationToken.None);

        Assert.Empty(new HtmlParser().ParseDocument(html).QuerySelectorAll(".facts button.term"));
    }

    [Fact]
    public void EveryVoteKindHasItsTermKeyOrNoneForOtherAndEveryKeyIsInTheGlossary()
    {
        var expected = new Dictionary<PublishedVoteKind, string?>
        {
            [PublishedVoteKind.Rule] = "house-rule",
            [PublishedVoteKind.PreviousQuestion] = "previous-question",
            [PublishedVoteKind.MotionToRecommit] = "motion-to-recommit",
            [PublishedVoteKind.Amendment] = "amendment",
            [PublishedVoteKind.Suspension] = "suspension-of-the-rules",
            [PublishedVoteKind.Passage] = "passage",
            [PublishedVoteKind.ConferenceReport] = "conference-report",
            [PublishedVoteKind.VetoOverride] = "veto-override",
            [PublishedVoteKind.Nomination] = "confirmation-vote",
            [PublishedVoteKind.Cloture] = "cloture",
            [PublishedVoteKind.MotionToProceed] = "motion-to-proceed",
            [PublishedVoteKind.Other] = null
        };

        Assert.Equal(Enum.GetValues<PublishedVoteKind>().Order(), expected.Keys.Order());
        foreach (var (kind, key) in expected)
        {
            Assert.Equal(key, PublishedRecordLabels.VoteKindTermKey(kind));
            if (key is not null) Assert.Equal(key, Glossary.Term(key).Key);
        }
    }

    [Theory]
    [InlineData(PublishedExecutiveActionKind.ExecutiveOrder, "term-executive-order")]
    [InlineData(PublishedExecutiveActionKind.Proclamation, "term-proclamation")]
    [InlineData(PublishedExecutiveActionKind.Memorandum, "term-memorandum")]
    [InlineData(PublishedExecutiveActionKind.Notice, "term-presidential-notice")]
    public async Task TheTypeOfAnExecutiveActionIsATermAndANoticeUsesThePresidentialNoticeDefinitionNotTheAgencyOne(PublishedExecutiveActionKind kind, string popover)
    {
        var record = (PublishedExecutiveAction)PublicApi.PublishedSamples.ExecutiveAction("Title", "Tail").Body with { Kind = kind };
        var html = await new ReleaseRenderer(null, null).RenderExecutiveActionAsync(record, CancellationToken.None);

        var document = new HtmlParser().ParseDocument(html);
        var type = document.QuerySelectorAll(".facts dt").Zip(document.QuerySelectorAll(".facts dd"))
            .Single(pair => pair.First.TextContent == "Type").Second;
        Assert.Equal(popover, type.QuerySelector("button.term")!.GetAttribute("popovertarget"));
        Assert.Equal(PublishedRecordLabels.ExecutiveActionKind(kind), type.ShownText());
    }

    [Fact]
    public void ANoticeIsLabeledPresidentialSoTheGlossaryLookupFindsTheRightDefinition() =>
        Assert.Equal(Glossary.Term("presidential-notice").Name, PublishedRecordLabels.ExecutiveActionKind(PublishedExecutiveActionKind.Notice));

    [Fact]
    public void EveryExecutiveActionKindHasATermInTheGlossary()
    {
        foreach (var kind in Enum.GetValues<PublishedExecutiveActionKind>())
            Assert.Equal(PublishedRecordLabels.ExecutiveActionTermKey(kind), Glossary.Term(PublishedRecordLabels.ExecutiveActionTermKey(kind)).Key);
    }

    [Fact]
    public async Task AnExecutiveActionShowsEachFactOnceWithALinkToItsSourceLineAndNoEvidencePanel()
    {
        var record = FullExecutiveAction();
        var html = await new ReleaseRenderer(null, null).RenderExecutiveActionAsync(record, CancellationToken.None);

        var document = new HtmlParser().ParseDocument(html);
        Assert.Null(document.QuerySelector("[aria-labelledby='evidence-title']"));
        Assert.Empty(document.QuerySelectorAll("blockquote"));
        var cited = record.Facts.Where(fact => fact.Label != ExecutiveActionFacts.Title).Select(fact => fact.Citation.Quote).Order(StringComparer.Ordinal);
        var linked = document.QuerySelectorAll(".facts a.source-line").Select(link => document.QuerySelector(link.GetAttribute("href")!)!.TextContent).Order(StringComparer.Ordinal);
        Assert.Equal(cited, linked);
    }

    private static PublishedExecutiveAction FullExecutiveAction()
    {
        var sample = (PublishedExecutiveAction)PublicApi.PublishedSamples.ExecutiveAction("Title", "Tail").Body;
        var notice = new PublishedExecutiveActionNotice { DocumentNumber = "2025-02000", PublishedOn = new DateOnly(2025, 2, 1), Url = "https://www.federalregister.gov/documents/2025/02/01/2025-02000/x" };
        (string Label, string Value)[] extra =
            [(ExecutiveActionFacts.Type, "Executive Order"), (ExecutiveActionFacts.President, "Official o1"), (ExecutiveActionFacts.PresidentialNumber, "14148"),
             (ExecutiveActionFacts.CorrectionNotice, notice.Value)];
        var text = sample.Sources[0].Text + "\n" + string.Join('\n', extra.Select(item => ExecutiveActionFacts.Line(item.Label, item.Value)));
        var facts = sample.Facts.Concat(extra.Select(item =>
        {
            var line = ExecutiveActionFacts.Line(item.Label, item.Value);
            return new PublishedExecutiveActionFact
            {
                Label = item.Label,
                Value = item.Value,
                Citation = sample.Facts[0].Citation with { Start = text.IndexOf(line, StringComparison.Ordinal), Length = line.Length, Quote = line }
            };
        }));
        return sample with { Facts = [.. facts], CorrectionNotices = [notice], Sources = [sample.Sources[0] with { Text = text }] };
    }

    [Fact]
    public async Task TheExecutiveActionSourceNoteNamesTheFederalRegisterAsATerm()
    {
        var record = (PublishedExecutiveAction)PublicApi.PublishedSamples.ExecutiveAction("Title", "Tail").Body;
        var html = await new ReleaseRenderer(null, null).RenderExecutiveActionAsync(record, CancellationToken.None);

        var sources = new HtmlParser().ParseDocument(html).QuerySelector("[aria-labelledby='sources-title']")!;
        Assert.Equal("term-federal-register", sources.QuerySelector("button.term")!.GetAttribute("popovertarget"));
        Assert.Equal("Federal Register", sources.QuerySelector("button.term")!.TextContent);
    }

    [Theory]
    [InlineData("Yea", "yea")]
    [InlineData("Aye", "yea")]
    [InlineData("Nay", "nay")]
    [InlineData("No", "nay")]
    [InlineData("Present", "present")]
    [InlineData("Not Voting", "not-voting")]
    [InlineData("Emmer", null)]
    public void EveryCastWordMapsToItsTermAndACandidateNameHasNone(string cast, string? key)
    {
        Assert.Equal(key, PublishedRecordLabels.CastTermKey(cast));
    }

    [Fact]
    public async Task TheWordCorrectionIsATermOnlyWhenTheCorrectionHasANote()
    {
        var corrections = new PublishedCorrection[]
        {
            new() { ReplacedRevisionNumber = 2, PublishedAtUtc = new DateTimeOffset(2026, 2, 4, 9, 0, 0, TimeSpan.Zero), Note = "Fixed a date." },
            new() { ReplacedRevisionNumber = 1, PublishedAtUtc = new DateTimeOffset(2026, 2, 2, 9, 0, 0, TimeSpan.Zero) }
        };
        var html = await new ReleaseRenderer(null, null).RenderActionAsync(Action() with { RevisionNumber = 3, Corrections = corrections }, null, [], PublicationCatalogs.None, CancellationToken.None);

        var items = new HtmlParser().ParseDocument(html).QuerySelectorAll("ul.corrections > li strong").ToArray();
        Assert.Equal("term-correction", items[0].QuerySelector("button.term")!.GetAttribute("popovertarget"));
        Assert.Equal("Correction", items[0].QuerySelector("button.term")!.TextContent);
        Assert.Null(items[1].QuerySelector("button.term"));
    }

    [Fact]
    public async Task TheRelatedPanelNamesTheStatementAsATerm()
    {
        var action = Action();
        var html = await new ReleaseRenderer(null, null).RenderActionAsync(action, null, [Relationship(PublishedRecordKind.Action, action.RecordId, '7')], PublicationCatalogs.None, CancellationToken.None);

        var button = new HtmlParser().ParseDocument(html).QuerySelector("[aria-labelledby='related-title'] button.term")!;
        Assert.Equal("term-statement", button.GetAttribute("popovertarget"));
        Assert.Equal("statement", button.TextContent);
    }

    // A bare count could mean all members who voted, so each heading says it counts tracked members of the chamber.
    [Fact]
    public async Task ASenateVoteCountsTrackedSenatorsAndEachMemberLinkNamesTheMemberLine()
    {
        var vote = VoteWithCasts([("Amy", "Yea"), ("Bo", "Yea")]) with { Chamber = PublishedChamber.Senate };
        var html = await new ReleaseRenderer(null, null).RenderVoteAsync(vote, null, [], PublicationCatalogs.None, CancellationToken.None);

        var members = new HtmlParser().ParseDocument(html).QuerySelector("[aria-labelledby='members-title']")!;
        Assert.Equal(["Voted Yea (2 tracked senators)"], members.QuerySelectorAll("h3").Select(heading => heading.ShownText()));
        Assert.Equal(["Source line: Amy, member line", "Source line: Bo, member line"], members.QuerySelectorAll("li a.small").Select(link => link.TextContent));
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
        var actionPage = new HtmlParser().ParseDocument(await renderer.RenderActionAsync(action, null, [actionLink], PublicationCatalogs.None, CancellationToken.None));
        var votePage = new HtmlParser().ParseDocument(await renderer.RenderVoteAsync(vote, null, [voteLink], PublicationCatalogs.None, CancellationToken.None));

        var rows = statementPage.QuerySelectorAll("[aria-labelledby='related-title'] li");
        Assert.Equal(2, rows.Length);
        var targetLink = rows[0].QuerySelectorAll("a");
        Assert.Equal(($"../records/{action.RecordId}.html", "Housing bill funds rent aid"), (targetLink[0].GetAttribute("href"), targetLink[0].TextContent));
        Assert.Equal($"../records/{actionLink.RecordId}.html", targetLink[1].GetAttribute("href"));
        Assert.Contains("Action, Describes own bill, statement of 1 May 2026", rows[0].TextContent, StringComparison.Ordinal);
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
        Assert.Empty(new HtmlParser().ParseDocument(await renderer.RenderActionAsync(Action(), null, [], PublicationCatalogs.None, CancellationToken.None))
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
            .ToDictionary(pair => pair.First.TextContent, pair => pair.Second.ShownText());
        Assert.Equal("Starts Monday 5 Oct 2026, New York time", facts["Week"]);
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

    [Fact]
    public async Task AllRecordsPageLinksEachKindToASearchAddressSoTheServiceAcceptsTheLink()
    {
        var release = ReleaseOf(3);
        release = release with
        {
            Records = [.. Enum.GetValues<PublishedRecordKind>().Reverse().Select((kind, index) => release.Records[0] with { Kind = kind, RecordId = new string((char)('a' + index), 64) })]
        };

        var html = await new ReleaseRenderer(null, null).RenderAllRecordsAsync(release, 1, CancellationToken.None);

        var document = new HtmlParser().ParseDocument(html);
        var panel = document.QuerySelector("section[aria-labelledby='panel-search-by-kind']")!;
        Assert.Equal("Search by kind", panel.QuerySelector("h2")!.TextContent);
        Assert.Contains("Each link opens the search page with that kind of record chosen.", panel.TextContent);
        var links = document.QuerySelectorAll("nav[aria-label='Search one kind of record'] a").ToList();
        Assert.Equal(["Document changes", "Statements", "Actions", "Weekly briefs", "Votes", "Relationships", "Executive actions"], links.Select(link => link.TextContent.Trim()));
        foreach (var link in links)
        {
            Assert.StartsWith("search.html?kind=", link.GetAttribute("href"), StringComparison.Ordinal);
            var value = link.GetAttribute("href")!["search.html?kind=".Length..];
            Assert.True(RecordKinds.TryParse(value, out _), value);
            Assert.Null(link.GetAttribute("aria-current"));
        }
        Assert.Contains("search.html\">Filter by issue or date</a>", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AllRecordsPageOfAnEmptyReleaseHasNoKindRowSoNoEmptyNavigationShows()
    {
        var html = await new ReleaseRenderer(null, null).RenderAllRecordsAsync(ReleaseOf(0), 1, CancellationToken.None);

        var document = new HtmlParser().ParseDocument(html);
        Assert.Null(document.QuerySelector("nav[aria-label='Search one kind of record']"));
        Assert.DoesNotContain("Search by kind", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AllRecordsPageOffersNoLinkForAKindWithoutRecordsSoNoLinkLeadsToAnEmptySearch()
    {
        var html = await new ReleaseRenderer(null, null).RenderAllRecordsAsync(ReleaseOf(2), 1, CancellationToken.None);

        var links = new HtmlParser().ParseDocument(html).QuerySelectorAll("nav[aria-label='Search one kind of record'] a");
        Assert.Equal(["search.html?kind=documentChange"], links.Select(link => link.GetAttribute("href")));
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

    [Fact]
    public async Task WithoutAnApiAddressNoPagedTableHasASortHeaderSoTheStaticPageStaysNewestFirstWithNoDeadControl()
    {
        var release = ReleaseOf(3) with { Records = [.. ReleaseOf(3).Records.Select(entry => entry with { IssueIds = ["housing"] })] };
        var renderer = new ReleaseRenderer(null, null);
        var facet = ReleaseFacets.From(release, PublicationCatalogs.None).Issues.Single();

        foreach (var html in new[] { await renderer.RenderAllRecordsAsync(release, 1, CancellationToken.None), await renderer.RenderFacetPageAsync(facet, 1, CancellationToken.None) })
        {
            Assert.DoesNotContain("data-sort", html, StringComparison.Ordinal);
            Assert.DoesNotContain("data-default-order", html, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task WithAnApiAddressTheAllRecordsHeadersNameTheSearchSortKeyAndTheStaticRowsStayInManifestOrder()
    {
        var release = ReleaseOf(120);

        var html = await new ReleaseRenderer("https://api.test", null).RenderAllRecordsAsync(release, 1, CancellationToken.None);

        var document = new HtmlParser().ParseDocument(html);
        Assert.Equal([("text", "headline"), ("date", "published")],
            document.QuerySelectorAll("thead th").Select(header => (header.GetAttribute("data-sort")!, header.GetAttribute("data-sort-key")!)));
        Assert.Equal("Published records. Newest first.", document.QuerySelector("caption")!.TextContent);
        var source = System.Text.Json.JsonDocument.Parse(document.QuerySelector("table")!.GetAttribute("data-sort-source")!).RootElement;
        Assert.Equal(("https://api.test", 120, "documentChange"), (source.GetProperty("api").GetString(), source.GetProperty("total").GetInt32(),
            source.GetProperty("kinds").EnumerateObject().Single(kind => kind.Value.GetString() == "Document change").Name));
        Assert.Equal(PublicationProtocol.PageSize, source.GetProperty("pageSize").GetInt32());
        Assert.Equal(["record", "published"], source.GetProperty("columns").EnumerateArray().Select(column => column.GetString()));
        Assert.Equal(release.Records.Take(50).Select(entry => entry.RecordId),
            Regex.Matches(html, "href=\"records/([0-9a-f]{32})\\.html\"").Select(match => match.Groups[1].Value));
        Assert.NotNull(document.QuerySelector("nav.pager"));
    }

    [Fact]
    public async Task AFacetPageSortsOnlyByRecordAndDateAndFiltersTheSearchByItsOwnIssueOrPolicyArea()
    {
        var release = ReleaseOf(3) with
        {
            Records = [.. ReleaseOf(3).Records.Select(entry => entry with { IssueIds = ["housing"], PolicyAreaIds = ["health-care"] })]
        };
        var facets = ReleaseFacets.From(release, PublicationCatalogs.None);
        var renderer = new ReleaseRenderer("https://api.test", null);

        async Task<(string[] Sortable, string Filter)> Read(ReleaseFacet facet)
        {
            var document = new HtmlParser().ParseDocument(await renderer.RenderFacetPageAsync(facet, 1, CancellationToken.None));
            var source = System.Text.Json.JsonDocument.Parse(document.QuerySelector("table")!.GetAttribute("data-sort-source")!).RootElement;
            return ([.. document.QuerySelectorAll("thead th[data-sort]").Select(header => header.TextContent.Trim())], source.GetProperty("filter").GetString()!);
        }

        var issue = await Read(facets.Issues.Single());
        Assert.Equal(["Record", "Date"], issue.Sortable);
        Assert.Equal("issue=housing", issue.Filter);
        Assert.Equal("policyArea=health-care", (await Read(facets.PolicyAreas.Single())).Filter);
    }

    private static PublishedRecordEntry FacetEntry(PublishedRecordKind kind, int index, PublishedVoteAlignment? alignment, DateTimeOffset published, string[] issues, string[] policyAreas) =>
        ReleaseOf(1).Records[0] with
        {
            Kind = kind,
            RecordId = index.ToString("x32"),
            FirstPublishedAtUtc = published,
            IssueIds = issues,
            PolicyAreaIds = policyAreas,
            Alignment = alignment
        };

    private static async Task<AngleSharp.Dom.IElement> FacetMainAsync(PublicationRelease release, FacetKind kind, int page)
    {
        var facets = ReleaseFacets.From(release, PublicationCatalogs.None);
        var facet = kind == FacetKind.Issue ? facets.Issues.Single() : facets.PolicyAreas.Single();
        return new HtmlParser().ParseDocument(await new ReleaseRenderer(null, null).RenderFacetPageAsync(facet, page, CancellationToken.None)).QuerySelector("main")!;
    }

    private static async Task<string> FacetSummaryTextAsync(PublicationRelease release, FacetKind kind, int page) =>
        (await FacetMainAsync(release, kind, page)).TextContent;

    [Fact]
    public async Task AnIssuePageSummaryCountsEveryPageOfItsFacetSoAVoteOnPageTwoStillCountsAndEachLineStatesWhatItCounts()
    {
        var newest = new DateTimeOffset(2026, 5, 11, 8, 0, 0, TimeSpan.Zero);
        var bills = Enumerable.Range(100, 60).Select(index => FacetEntry(PublishedRecordKind.Action, index, null, newest.AddMinutes(index), ["housing"], []));
        PublishedRecordEntry Vote(int index, PublishedVoteAlignment? alignment) =>
            FacetEntry(PublishedRecordKind.Vote, index, alignment, newest.AddDays(-index), ["housing"], []);
        PublishedRecordEntry[] votes = [Vote(1, PublishedVoteAlignment.Split), Vote(2, PublishedVoteAlignment.Split), Vote(3, PublishedVoteAlignment.PartiesAgree),
            Vote(4, PublishedVoteAlignment.Mixed), Vote(5, null)];
        var release = ReleaseOf(0) with { Records = [.. bills, .. votes] };

        var text = await FacetSummaryTextAsync(release, FacetKind.Issue, 1);

        Assert.DoesNotContain("Vote headline", text);
        Assert.Contains("Votes and bills tagged with this issue", text);
        Assert.Contains("5 roll call votes on this site are tagged with this issue.", text);
        Assert.Contains("2 of them were split votes. A split vote is a substantive roll call vote where most Democrats and most Republicans who cast Yea or Nay voted on opposite sides.", text);
        Assert.Contains("1 of them was a vote where both parties agreed. A vote where both parties agreed is a substantive roll call vote where most Democrats and most Republicans who cast Yea or Nay voted the same way.", text);
        Assert.Contains("2 of them are in neither group.", text);
        Assert.Contains("60 bills and resolutions on this site are tagged with this issue.", text);
        Assert.Contains("An AI model picks the issue tag of a bill or vote, and no person checks it", text);
        Assert.Single(text.Split("It does not show a position on the issue.").Skip(1));
        var links = (await FacetMainAsync(release, FacetKind.Issue, 1)).QuerySelectorAll("a").Select(link => link.GetAttribute("href")).ToArray();
        Assert.Contains("../method.html#issue-tags", links);
        Assert.Contains("../method.html#vote-badges", links);
    }

    [Fact]
    public async Task AnIssuePageSummaryOmitsTheSplitAgreedAndOtherLinesWhenTheirCountIsZeroAndOnlyTheFirstPageShowsThePanel()
    {
        var published = new DateTimeOffset(2026, 5, 11, 8, 0, 0, TimeSpan.Zero);
        var release = ReleaseOf(0) with
        {
            Records = [.. Enumerable.Range(0, 51).Select(index => FacetEntry(PublishedRecordKind.Action, index, null, published.AddMinutes(index), ["housing"], []))]
        };

        var first = await FacetSummaryTextAsync(release, FacetKind.Issue, 1);

        Assert.Contains("No roll call vote on this site is tagged with this issue.", first);
        Assert.Contains("51 bills and resolutions on this site are tagged with this issue.", first);
        Assert.DoesNotContain("split vote", first);
        Assert.DoesNotContain("both parties agreed", first);
        Assert.DoesNotContain("neither group", first);
        Assert.DoesNotContain("Votes and bills tagged", await FacetSummaryTextAsync(release, FacetKind.Issue, 2));
    }

    [Fact]
    public async Task APolicyAreaPageSummaryCountsOnlyBillsBecauseAVoteEntryNeverCarriesAPolicyArea()
    {
        var published = new DateTimeOffset(2026, 5, 11, 8, 0, 0, TimeSpan.Zero);
        var release = ReleaseOf(0) with
        {
            Records =
            [
                FacetEntry(PublishedRecordKind.Action, 1, null, published, ["housing"], ["health-care"]),
                FacetEntry(PublishedRecordKind.Vote, 2, PublishedVoteAlignment.Split, published, ["housing"], [])
            ]
        };

        var text = await FacetSummaryTextAsync(release, FacetKind.PolicyArea, 1);

        Assert.Contains("Bills tagged with this policy area", text);
        Assert.Contains("1 bill or resolution on this site is tagged with this policy area.", text);
        Assert.DoesNotContain("roll call", text);
        Assert.DoesNotContain("split vote", text);
        Assert.DoesNotContain("AI model", text);
    }

    [Fact]
    public async Task EachSortedTableNamesItsColumnsInTheOrderOfItsHeadersBecauseTheScriptBuildsRowsFromThatList()
    {
        var release = ReleaseOf(3) with { Records = [.. ReleaseOf(3).Records.Select(entry => entry with { IssueIds = ["housing"] })] };
        var renderer = new ReleaseRenderer("https://api.test", null);
        var headerOf = new Dictionary<string, string> { ["record"] = "Record", ["officials"] = "Officials", ["date"] = "Date", ["published"] = "First published" };

        foreach (var html in new[]
        {
            await renderer.RenderAllRecordsAsync(release, 1, CancellationToken.None),
            await renderer.RenderFacetPageAsync(ReleaseFacets.From(release, PublicationCatalogs.None).Issues.Single(), 1, CancellationToken.None)
        })
        {
            var document = new HtmlParser().ParseDocument(html);
            var source = System.Text.Json.JsonDocument.Parse(document.QuerySelector("table")!.GetAttribute("data-sort-source")!).RootElement;
            Assert.Equal(document.QuerySelectorAll("thead th").Select(header => header.TextContent.Trim()),
                source.GetProperty("columns").EnumerateArray().Select(column => headerOf[column.GetString()!]));
        }
    }

    [Fact]
    public async Task TablesThatDoNotPassASortStayPlainEvenWhenTheReleaseHasAnApiAddress()
    {
        var renderer = new ReleaseRenderer("https://api.test", null);

        var home = await renderer.RenderHomeAsync(ReleaseOf(2), null, NoFacets, NoHighlights(ReleaseOf(2)), CancellationToken.None);
        var briefs = await renderer.RenderBriefsAsync(ReleaseOfBriefs(2), [.. ReleaseOfBriefs(2).Records], 1, CancellationToken.None);

        Assert.DoesNotContain("data-sort", home, StringComparison.Ordinal);
        Assert.DoesNotContain("data-sort", briefs, StringComparison.Ordinal);
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

        var html = await new ReleaseRenderer(null, null).RenderHomeAsync(release, brief, NoFacets, NoHighlights(release), CancellationToken.None);

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
    public async Task HomeHidesTheBriefingPanelWhenNoBriefExistsInsteadOfShowingAnEmptyPanel()
    {
        var renderer = new ReleaseRenderer(null, null);

        var with = new HtmlParser().ParseDocument(await renderer.RenderHomeAsync(ReleaseOf(1), Brief(), NoFacets, NoHighlights(ReleaseOf(1)), CancellationToken.None));
        var without = new HtmlParser().ParseDocument(await renderer.RenderHomeAsync(ReleaseOf(1), null, NoFacets, NoHighlights(ReleaseOf(1)), CancellationToken.None));

        Assert.Contains("A week of <housing> news", with.QuerySelector("main")!.TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain("A week of <housing> news", without.QuerySelector("main")!.TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain("briefing", without.QuerySelector("main")!.TextContent, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TheUnderstandABillButtonLandsOnTheBillsAndCongressGroupOfTheGlossaryInsteadOfTheTopOfALongPage()
    {
        var renderer = new ReleaseRenderer(null, null);
        var home = new HtmlParser().ParseDocument(await renderer.RenderHomeAsync(ReleaseOf(1), null, NoFacets, NoHighlights(ReleaseOf(1)), CancellationToken.None));
        var glossary = new HtmlParser().ParseDocument(await renderer.RenderGlossaryAsync(CancellationToken.None));

        var anchor = home.QuerySelector(".home-actions a[href^='glossary.html#']")!.GetAttribute("href")!.Split('#')[1];

        Assert.Equal("Bills and Congress", glossary.QuerySelector($"h2[id='{anchor}']")!.TextContent);
    }

    [Fact]
    public async Task HomeLabelsTheBriefPanelAsAWeeklyBriefOnlyWhenABriefExists()
    {
        var renderer = new ReleaseRenderer(null, null);

        var with = new HtmlParser().ParseDocument(await renderer.RenderHomeAsync(ReleaseOf(1), Brief(), NoFacets, NoHighlights(ReleaseOf(1)), CancellationToken.None));
        var without = new HtmlParser().ParseDocument(await renderer.RenderHomeAsync(ReleaseOf(1), null, NoFacets, NoHighlights(ReleaseOf(1)), CancellationToken.None));

        Assert.Equal("A week of <housing> news", with.QuerySelectorAll("main p.muted.small").Single(label => label.TextContent == "Weekly brief").NextElementSibling!.QuerySelector("h2")!.TextContent);
        Assert.DoesNotContain("Weekly brief", without.QuerySelector("main")!.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheMethodPageSaysThatTheClosestVoteCardDoesNotAppearWhenNoVoteFitsInsteadOfClaimingItSaysSo()
    {
        var text = new HtmlParser().ParseDocument(await new ReleaseRenderer(null, null).RenderMethodAsync(CancellationToken.None)).QuerySelector("main")!.TextContent;

        Assert.Contains("It names the dates. With no such vote in these dates, the card does not appear.", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HomeSaysWhatTheSiteIsAndOffersThreeFirstStepsSoANewReaderKnowsWhereToStart()
    {
        var html = await new ReleaseRenderer(null, null).RenderHomeAsync(ReleaseOf(1), null, NoFacets, NoHighlights(ReleaseOf(1)), CancellationToken.None);

        var document = new HtmlParser().ParseDocument(html);
        Assert.Equal("Civic Lens", document.Title);
        Assert.Equal("Civic Lens", document.QuerySelector("h1")!.TextContent);
        Assert.Equal("Civic Lens shows what U.S. officials say and do, using official records you can check yourself.", document.QuerySelector(".home-lead")!.TextContent);
        Assert.Equal([("Find your representative or senators", "officials.html"), ("Understand a bill", "glossary.html#glossary-group-bills-and-congress"), ("Search all records", "search.html")],
            document.QuerySelectorAll(".home-actions a").Select(link => (link.TextContent, link.GetAttribute("href")!)));
        Assert.Matches("^Release 4, published ", document.QuerySelector(".home-lead + p.small")!.TextContent);
    }

    [Fact]
    public async Task HomeLinksTheListOfAllWeeklyBriefsOnlyWhenABriefExistsSoNoLinkLeadsToAnEmptyList()
    {
        var renderer = new ReleaseRenderer(null, null);

        var withBrief = new HtmlParser().ParseDocument(await renderer.RenderHomeAsync(ReleaseOf(1), Brief(), NoFacets, NoHighlights(ReleaseOf(1)), CancellationToken.None));
        var without = new HtmlParser().ParseDocument(await renderer.RenderHomeAsync(ReleaseOf(1), null, NoFacets, NoHighlights(ReleaseOf(1)), CancellationToken.None));

        Assert.Equal("All weekly briefs", withBrief.QuerySelector("main a[href='briefs.html']")!.TextContent);
        Assert.Null(without.QuerySelector("main a[href='briefs.html']"));
    }

    [Fact]
    public async Task BriefsPageListsTheBriefsInTheGivenOrderWithTheWeekAndALinkToEachBriefPage()
    {
        var release = ReleaseOfBriefs(3) with { Records = [.. ReleaseOfBriefs(3).Records.Reverse()] };

        var html = await new ReleaseRenderer(null, null).RenderBriefsAsync(release, release.Records, 1, CancellationToken.None);

        var document = new HtmlParser().ParseDocument(html);
        var rows = document.QuerySelectorAll("tbody tr").ToList();
        Assert.Equal(new[] { 2, 1, 0 }.Select(index => $"records/{index:x64}.html"), rows.Select(row => row.QuerySelector("a.cell-title")!.GetAttribute("href")));
        Assert.Equal(["Brief 2", "Brief 1", "Brief 0"], rows.Select(row => row.QuerySelector("a.cell-title")!.TextContent));
        Assert.Equal(["19 Jan 2026", "12 Jan 2026", "5 Jan 2026"], rows.Select(row => row.QuerySelector("td[data-label='Week of']")!.TextContent.Trim()));
        Assert.Equal(["Record", "Week of", "First published"], document.QuerySelectorAll("thead th").Select(header => header.TextContent.Trim()));
        Assert.Empty(document.QuerySelectorAll("tbody .badge"));
    }

    [Fact]
    public async Task BriefsPageSaysThereAreNoBriefsAndOffersAllRecordsWhenTheReleaseHasNone()
    {
        var html = await new ReleaseRenderer(null, null).RenderBriefsAsync(ReleaseOf(2), [], 1, CancellationToken.None);

        var document = new HtmlParser().ParseDocument(html);
        Assert.Equal("No weekly briefs yet", document.QuerySelector("main .empty h3")!.TextContent.Trim());
        Assert.Equal("all.html", document.QuerySelector("main a[href='all.html']")!.GetAttribute("href"));
        Assert.Empty(document.QuerySelectorAll("table"));
    }

    [Theory]
    [InlineData(1, 50, "Page 1 of 2", "briefs-2.html")]
    [InlineData(2, 5, "Page 2 of 2", null)]
    public async Task BriefsPagePagesPastFiftyBriefsWithPagerLinksWhereTheyApply(int page, int expectedCount, string label, string? next)
    {
        var html = await new ReleaseRenderer(null, null).RenderBriefsAsync(ReleaseOfBriefs(55), ReleaseOfBriefs(55).Records, page, CancellationToken.None);

        var document = new HtmlParser().ParseDocument(html);
        Assert.Equal(expectedCount, document.QuerySelectorAll("tbody tr").Length);
        Assert.Contains(label, html, StringComparison.Ordinal);
        Assert.Equal(next, document.QuerySelector("a[rel='next']")?.GetAttribute("href"));
        Assert.Equal(page == 2 ? "briefs.html" : null, document.QuerySelector("a[rel='prev']")?.GetAttribute("href"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task BriefsPageRejectsAPageOutsideTheList(int page)
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            new ReleaseRenderer(null, null).RenderBriefsAsync(ReleaseOfBriefs(3), ReleaseOfBriefs(3).Records, page, CancellationToken.None));
    }

    [Fact]
    public async Task AboutPageLinksToTheMethodAndReportPagesAndNamesTheWordingLabelItPromises()
    {
        var html = await new ReleaseRenderer(null, null).RenderAboutAsync(CancellationToken.None);

        var document = new HtmlParser().ParseDocument(html);
        Assert.NotNull(document.QuerySelector("main a[href='method.html']"));
        Assert.NotNull(document.QuerySelector("main a[href='report.html']"));
        Assert.Contains(PublishedRecordLabels.ModelWording, html, StringComparison.Ordinal);
        Assert.Contains(PublishedRecordLabels.CodeWording, html, StringComparison.Ordinal);
        Assert.Contains("On an action page or a vote page, a summary that a model wrote has the label \"An AI summary of the official record\". " +
            "Wording that code wrote from the bill title, the sponsor, and the introduction date has the label \"Written automatically from Congress.gov data, with no AI\".",
            new HtmlParser().ParseDocument(html).QuerySelector("main")!.TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain("unreviewed", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AboutPageNamesVoteviewAsASenateVoteSourceAndCarriesTheCitationThatVoteviewAsksFor()
    {
        var text = new HtmlParser().ParseDocument(await new ReleaseRenderer(null, null).RenderAboutAsync(CancellationToken.None)).QuerySelector("main")!.TextContent;

        Assert.Contains("Senate votes come from Voteview, an academic database at UCLA. Voteview is not an official source, so each Senate vote page links the official Senate roll call.", text, StringComparison.Ordinal);
        Assert.Contains("Lewis, Jeffrey B., Keith Poole, Howard Rosenthal, Adam Boche, Aaron Rudkin, and Luke Sonnet (2026). Voteview: Congressional Roll-Call Votes Database. https://voteview.com/",
            text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheMethodPageSaysWhereSenateVotesComeFromAndThatNoVicePresidentTieBreakVoteIsListed()
    {
        var text = new HtmlParser().ParseDocument(await new ReleaseRenderer(null, null).RenderMethodAsync(CancellationToken.None)).QuerySelector("main")!.TextContent;

        Assert.Contains("Senate votes come from Voteview, an academic database at UCLA", text, StringComparison.Ordinal);
        Assert.Contains("Each vote page also links the official roll call page on senate.gov.", text, StringComparison.Ordinal);
        Assert.Contains("Voteview does not list a Vice President tie-break vote", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AboutPageGivesAContactAddressSoAnOfficialOrReaderCanReachTheOwner()
    {
        var document = new HtmlParser().ParseDocument(await new ReleaseRenderer(null, null).RenderAboutAsync(CancellationToken.None));

        Assert.NotNull(document.QuerySelector("main a[href='mailto:contact@civic-lens.info']"));
        Assert.Contains("not part of any government office, party, campaign or company", document.QuerySelector("main")!.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AboutPageStatesTheClosedReportRetentionAndFailsWhenTheRuleNoLongerSaysOneYear()
    {
        var document = new HtmlParser().ParseDocument(await new ReleaseRenderer(null, null).RenderAboutAsync(CancellationToken.None));

        Assert.Equal(365, ReaderReportProtocol.ClosedRetentionDays);
        Assert.Contains("we delete it from the review system after one year", document.QuerySelector("main")!.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AboutPagePrivacyTableNamesWhatIsKeptForEachActionSoNoReaderIsLeftToGuess()
    {
        var document = new HtmlParser().ParseDocument(await new ReleaseRenderer(null, null).RenderAboutAsync(CancellationToken.None));

        var table = Assert.Single(document.QuerySelectorAll("table"), candidate => candidate.QuerySelector("caption")!.TextContent == "What we keep and who else sees it");
        Assert.Equal(["When you", "What we keep", "Who else sees it"], table.QuerySelectorAll("thead th").Select(cell => cell.TextContent));
        Assert.Equal(["Read a page", "Pick light or dark", "Search", "Report a problem", "Email us"], table.QuerySelectorAll("tbody tr").Select(row => row.FirstElementChild!.TextContent));
        Assert.Contains($"deleted after {ReaderReportProtocol.PurgeAfterDays} days", document.QuerySelector("main")!.TextContent, StringComparison.Ordinal);
        Assert.NotNull(document.QuerySelector("main a[href='https://www.cloudflare.com/privacypolicy/']"));
    }

    [Fact]
    public async Task AboutPageStatesTheDataAndRecordLicenseWithALinkAndOwnsTheCodeLicenseInTheTable()
    {
        var document = new HtmlParser().ParseDocument(await new ReleaseRenderer(null, null).RenderAboutAsync(CancellationToken.None));

        var table = Assert.Single(document.QuerySelectorAll("table"), candidate => candidate.QuerySelector("caption")!.TextContent == "Terms for each kind of material");
        Assert.Equal(["Our records, summaries and weekly briefs", "Our data files", "Words we quote from officials and Congress", "Senate vote data from Voteview", "Official portraits", "Source code"],
            table.QuerySelectorAll("tbody tr").Select(row => row.FirstElementChild!.TextContent));
        var license = document.QuerySelector("main a[href='https://creativecommons.org/licenses/by/4.0/']");
        Assert.Equal("CC BY 4.0", license!.TextContent);
        Assert.Equal("noopener noreferrer", license.GetAttribute("rel"));
        Assert.NotNull(document.QuerySelector("main a[href='https://github.com/young-kobe/civic-lens']"));
        Assert.Single(Regex.Matches(document.QuerySelector("main")!.TextContent, "AGPL-3.0"));
    }

    [Fact]
    public async Task HomeAndTheFacetListLinkOnlyFacetsThatHavePagesAndShowNamesAndCounts()
    {
        var release = ReleaseOf(3) with { Records = [.. ReleaseOf(3).Records.Select(entry => entry with { IssueIds = ["housing", "Not A Path"] })] };
        var facets = ReleaseFacets.From(release, new PublicationCatalog(new Dictionary<string, string>(), IssuesNamed("housing", "Housing costs")));

        var home = await new ReleaseRenderer(null, null).RenderHomeAsync(release, null, facets, NoHighlights(release), CancellationToken.None);
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
        Assert.Equal(["Headline c Document change||12 May 2026", "Headline d Document change|Ann Roe|10 May 2026",
            "Headline b Document change|Jane Doe|10 May 2026", "Headline a Document change|Jane Doe, Ann Roe|2 Jan 2026"], rows);
    }

    [Fact]
    public async Task EveryRecordKindListsItsDatedEventsInDateOrderOnATimeline()
    {
        var statement = await TimelineOf(renderer => renderer.RenderStatementAsync(Statement(), [], PublicationCatalogs.None, CancellationToken.None));
        var change = await TimelineOf(renderer => renderer.RenderRecordAsync(Record(), PublicationCatalogs.None, CancellationToken.None));
        var action = await TimelineOf(renderer => renderer.RenderActionAsync(Action(), null, [], PublicationCatalogs.None, CancellationToken.None));
        var vote = await TimelineOf(renderer => renderer.RenderVoteAsync(Vote(), null, [], PublicationCatalogs.None, CancellationToken.None));
        var relationship = await TimelineOf(renderer => renderer.RenderRelationshipAsync(
            Relationship(PublishedRecordKind.Action, new string('a', 64), '7') with { StatementDate = new DateOnly(2026, 2, 3) }, PublicationCatalogs.None, CancellationToken.None));
        var brief = await TimelineOf(renderer => renderer.RenderBriefAsync(Brief(), CancellationToken.None));

        Assert.Equal(["1 May 2026|Statement made", "3 May 2026|Source observed, 3 May 2026, 10:45 UTC",
            "10 May 2026|Approved, 10 May 2026, 12:00 UTC", "11 May 2026|First published, 11 May 2026, 08:00 UTC"], statement);
        Assert.Equal(["1 Mar 2026|The source changed", "2 Apr 2026|Earlier version observed, 2 Apr 2026, 09:30 UTC",
            "3 May 2026|Later version observed, 3 May 2026, 10:45 UTC", "10 May 2026|Approved, 10 May 2026, 12:00 UTC",
            "11 May 2026|First published, 11 May 2026, 08:00 UTC"], change);
        Assert.Equal(["3 Jan 2026|Sponsored by Jane <Doe> (sponsor)", "5 Jan 2026|Sponsored by Ann Roe (cosponsor)",
            "20 Jan 2026|Withdrawn by Ann Roe (cosponsor)", "22 Jan 2026|Approved, 22 Jan 2026, 12:00 UTC",
            "23 Jan 2026|First published, 23 Jan 2026, 08:00 UTC"], action);
        Assert.Equal(["10 Apr 2025|Vote held", "22 Jan 2026|Approved, 22 Jan 2026, 12:00 UTC", "23 Jan 2026|First published, 23 Jan 2026, 08:00 UTC"], vote);
        Assert.Equal(["3 Feb 2026|Statement made", "4 Feb 2026|Approved, 4 Feb 2026, 12:00 UTC", "5 Feb 2026|First published, 5 Feb 2026, 08:00 UTC"],
            relationship);
        Assert.Equal(["5 Oct 2026|Week starts", "6 Oct 2026|Approved, 6 Oct 2026, 12:00 UTC", "7 Oct 2026|First published, 7 Oct 2026, 08:00 UTC"], brief);
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

        foreach (var html in new[] { await renderer.RenderActionAsync(action, null, [], PublicationCatalogs.None, CancellationToken.None),
                     await renderer.RenderStatementAsync(statement, [], PublicationCatalogs.None, CancellationToken.None) })
        {
            var entries = new HtmlParser().ParseDocument(html).QuerySelectorAll("ul.corrections > li")
                .Select(item => (item.QuerySelector("strong")!.ShownText(), item.QuerySelector("p")?.TextContent)).ToArray();
            Assert.Equal(("Correction, 4 Feb 2026. Replaces revision 2.", "Fixed the <sponsor> date."), entries[0]);
            Assert.Equal(("Updated from the source, 2 Feb 2026. Replaces revision 1.", null), entries[1]);
        }
        var timeline = await TimelineOf(renderer => renderer.RenderActionAsync(action, null, [], PublicationCatalogs.None, CancellationToken.None));
        Assert.Contains("4 Feb 2026|Correction, replaces revision 2, 4 Feb 2026, 09:00 UTC", timeline);
        Assert.Contains("2 Feb 2026|Updated from the source, replaces revision 1, 2 Feb 2026, 09:00 UTC", timeline);
    }

    [Fact]
    public async Task AVoteWhoseTextAloneChangedSaysSoOnThePageAndInTheTimeline()
    {
        var vote = Vote() with
        {
            RevisionNumber = 2,
            Corrections = [new() { ReplacedRevisionNumber = 1, PublishedAtUtc = new DateTimeOffset(2026, 2, 2, 9, 0, 0, TimeSpan.Zero), FactsUnchanged = true }]
        };

        var html = await new ReleaseRenderer(null, null).RenderVoteAsync(vote, null, [], PublicationCatalogs.None, CancellationToken.None);

        var entry = new HtmlParser().ParseDocument(html).QuerySelector("ul.corrections > li strong")!.ShownText();
        Assert.Equal("Summary rewritten by the AI model, 2 Feb 2026. Replaces revision 1. The vote facts did not change.", entry);
        var timeline = await TimelineOf(renderer => renderer.RenderVoteAsync(vote, null, [], PublicationCatalogs.None, CancellationToken.None));
        Assert.Contains("2 Feb 2026|Summary rewritten by the AI model, replaces revision 1, 2 Feb 2026, 09:00 UTC. The vote facts did not change.", timeline);
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
        foreach (var expected in new[] { "Action", new string('c', 64), "3", "1 Feb 2026, 08:00 UTC", "9 Feb 2026, 09:30 UTC", "Wrong <bill>." })
            Assert.Contains(expected, text, StringComparison.Ordinal);
        Assert.Contains("Wrong &lt;bill&gt;.", html, StringComparison.Ordinal);
        Assert.Equal("Correction, 4 Feb 2026. Replaces revision 2.", document.QuerySelector("ul.corrections > li strong")!.ShownText());
        Assert.Empty(document.QuerySelectorAll("main a[href*='report.html?']"));
    }

    [Fact]
    public async Task AReleaseWithNoRecordLeftStillRendersItsIndexPagesSoAWithdrawalOfTheLastRecordPublishes()
    {
        var release = ReleaseOf(0);
        var facets = ReleaseFacets.From(release, PublicationCatalogs.None);
        var renderer = new ReleaseRenderer(null, null);

        Assert.NotEmpty(await renderer.RenderHomeAsync(release, null, facets, NoHighlights(release), CancellationToken.None));
        Assert.NotEmpty(await renderer.RenderAllRecordsAsync(release, 1, CancellationToken.None));
        Assert.NotEmpty(await renderer.RenderSearchAsync(release, facets, CancellationToken.None));
        Assert.NotEmpty(await renderer.RenderFacetsAsync(facets, CancellationToken.None));
    }

    [Fact]
    public async Task ARecordWithNoCorrectionsShowsNoCorrectionList()
    {
        var html = await new ReleaseRenderer(null, null).RenderActionAsync(Action(), null, [], PublicationCatalogs.None, CancellationToken.None);

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

        Assert.Equal(["10 Apr 2025|Vote held", "22 Jan 2026|First published, 22 Jan 2026, 08:00 UTC", "22 Jan 2026|Approved, 22 Jan 2026, 12:00 UTC"], events);
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
    public async Task EveryPageTypeHasABoundedDescriptionThatOpenGraphRepeats()
    {
        var pages = (await RenderEveryPageTypeAsync()).Select(page => page.Html);

        foreach (var html in pages)
        {
            var head = new HtmlParser().ParseDocument(html).Head!;
            var description = head.QuerySelector("meta[name='description']")!.GetAttribute("content")!;
            Assert.InRange(description.Length, 10, 160);
            Assert.Equal(description, head.QuerySelector("meta[property='og:description']")!.GetAttribute("content"));
            var title = head.QuerySelector("title")!.TextContent;
            Assert.True(title == "Civic Lens" || title.EndsWith(" | Civic Lens", StringComparison.Ordinal), title);
            Assert.Equal(head.QuerySelector("title")!.TextContent, head.QuerySelector("meta[property='og:title']")!.GetAttribute("content"));
            Assert.Equal("Civic Lens", head.QuerySelector("meta[property='og:site_name']")!.GetAttribute("content"));
            Assert.Contains("favicon.svg", head.QuerySelector("link[rel='icon']")!.GetAttribute("href"), StringComparison.Ordinal);
        }
    }

    private const string BaseUrl = "https://civic.example";

    [Fact]
    public async Task EveryPageTypeNamesItsOwnAddressAsCanonicalAndOpenGraphUrlSoSearchEnginesKeepOneAddressForEachPage()
    {
        foreach (var (_, path, html, _) in await RenderEveryPageTypeAsync(PublicBaseUrl.Parse(BaseUrl, "V")))
        {
            var head = new HtmlParser().ParseDocument(html).Head!;
            var expected = path == PublicationProtocol.HomePath ? BaseUrl + "/" : $"{BaseUrl}/{path}";

            Assert.Equal(expected, head.QuerySelector("link[rel='canonical']")!.GetAttribute("href"));
            Assert.Equal(expected, head.QuerySelector("meta[property='og:url']")!.GetAttribute("content"));
            Assert.Equal(1, head.QuerySelectorAll("link[rel='canonical']").Length);
        }
    }

    [Fact]
    public async Task TheSearchPageNamesTheBarePageAsCanonicalSoAQueryInTheAddressNeverBecomesAnotherPage()
    {
        var page = (await RenderEveryPageTypeAsync(PublicBaseUrl.Parse(BaseUrl, "V"))).Single(page => page.Name == "search");

        var head = new HtmlParser().ParseDocument(page.Html).Head!;

        Assert.Equal(BaseUrl + "/search.html", head.QuerySelector("link[rel='canonical']")!.GetAttribute("href"));
    }

    [Fact]
    public async Task APagePastTheFirstNamesItsOwnPageAsCanonicalSoPagesAreNotMergedIntoTheFirst()
    {
        var renderer = new ReleaseRenderer(null, null, PublicBaseUrl.Parse(BaseUrl, "V"));
        var facet = SearchFacets().Issues[0];
        var paged = facet with { Records = [.. Enumerable.Repeat(facet.Records[0], PublicationProtocol.PageSize + 1)] };

        var html = await renderer.RenderFacetPageAsync(paged, 2, CancellationToken.None);

        Assert.Equal($"{BaseUrl}/{paged.PagePath(2)}",
            new HtmlParser().ParseDocument(html).QuerySelector("link[rel='canonical']")!.GetAttribute("href"));
    }

    [Fact]
    public async Task WithoutABaseUrlNoPageHasACanonicalOrOpenGraphUrlSoNoPageNamesAnAddressThatMayBeWrong()
    {
        foreach (var (name, _, html, _) in await RenderEveryPageTypeAsync())
        {
            var head = new HtmlParser().ParseDocument(html).Head!;

            Assert.True(head.QuerySelector("link[rel='canonical']") is null && head.QuerySelector("meta[property='og:url']") is null, name);
        }
    }

    [Fact]
    public async Task OnlyPagesThatAReaderShouldNotLandOnFromASearchEngineAskToBeSkipped()
    {
        string[] skipped = ["withdrawal", "search", "report"];
        foreach (var (name, _, html, _) in await RenderEveryPageTypeAsync(PublicBaseUrl.Parse(BaseUrl, "V")))
            Assert.Equal(skipped.Contains(name) ? "noindex" : null,
                new HtmlParser().ParseDocument(html).Head!.QuerySelector("meta[name='robots']")?.GetAttribute("content"));

        var notFound = new HtmlParser().ParseDocument(await new ReleaseRenderer(null, null, PublicBaseUrl.Parse(BaseUrl, "V"))
            .RenderNotFoundAsync(CancellationToken.None));
        Assert.Equal("noindex", notFound.Head!.QuerySelector("meta[name='robots']")!.GetAttribute("content"));
        Assert.Null(notFound.Head.QuerySelector("link[rel='canonical']"));
        Assert.Null(notFound.Head.QuerySelector("meta[property='og:url']"));
    }

    [Fact]
    public async Task EveryAssetNavigationAndFooterLinkOfTheNotFoundPageIsAbsoluteBecauseTheCdnServesItAtAnyMissingPath()
    {
        var html = await new ReleaseRenderer(null, null, PublicBaseUrl.Parse(BaseUrl, "V")).RenderNotFoundAsync(CancellationToken.None);

        var document = new HtmlParser().ParseDocument(html);
        var addresses = document.QuerySelectorAll("[href], [src]").Select(element => element.GetAttribute("href") ?? element.GetAttribute("src")!)
            .Where(address => !address.StartsWith('#')).ToList();
        Assert.NotEmpty(addresses);
        Assert.All(addresses, address => Assert.StartsWith(BaseUrl + "/", address, StringComparison.Ordinal));
        Assert.Equal("Page not found | Civic Lens", document.QuerySelector("title")!.TextContent);
        Assert.Contains("does not exist on Civic Lens", document.QuerySelector("main")!.TextContent, StringComparison.Ordinal);
        Assert.Contains($"{BaseUrl}/index.html", addresses);
        Assert.Contains($"{BaseUrl}/officials.html", addresses);
        Assert.Contains($"{BaseUrl}/search.html", addresses);
    }

    [Fact]
    public async Task TheNotFoundPageNeedsABaseUrlBecauseItCannotFindItsAssetsWithoutOne()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => new ReleaseRenderer(null, null).RenderNotFoundAsync(CancellationToken.None));
    }

    // Written by ReleaseIndexPages and ReleaseOfficialPages. PublishReleaseTests pins them.
    private static readonly string[] WrittenRootPages =
    [
        PublicationProtocol.HomePath, PublicationProtocol.OfficialsPagePath, PublicationProtocol.FacetsPath,
        PublicationProtocol.AllRecordsPagePath(1), PublicationProtocol.BriefsPagePath(1), PublicationProtocol.SearchPagePath,
        PublicationProtocol.MethodPagePath, PublicationProtocol.AboutPagePath, PublicationProtocol.GlossaryPagePath, PublicationProtocol.ReportPagePath,
        .. OfficialBoard.All.Select(board => board.PagePath)
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

            Assert.Equal(11, links.Count);
            Assert.All(links, href => Assert.True(WrittenRootPages.Contains(ReleasePath(pagePath, href)), $"{name}: {href}"));
        }
    }

    private static async Task<(string Name, string Path, string Html, string? Section)[]> RenderEveryPageTypeAsync(PublicBaseUrl? baseUrl = null)
    {
        var renderer = new ReleaseRenderer(null, null, baseUrl);
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
            Terms = [new() { Office = "Representative", Kind = PublishedOfficeKind.Representative, Party = "Republican", Start = new DateOnly(2025, 1, 3) }]
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
        var official = new ReleaseOfficialPage(profile, true, 119, true, [], [.. entries], [.. votes], new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero), TalkAndActionProfile.Of(119, entries, votes));
        var pastOfficial = new ReleaseOfficialPage(profile, true, 118, false, [], [], [], new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero));
        return
        [
            ("record", PublicationProtocol.RecordPagePath(Record().RecordId), await renderer.RenderRecordAsync(Record(), PublicationCatalogs.None, CancellationToken.None), "All records"),
            ("statement", PublicationProtocol.RecordPagePath(Statement().RecordId), await renderer.RenderStatementAsync(Statement(), [], PublicationCatalogs.None, CancellationToken.None), "All records"),
            ("action", PublicationProtocol.RecordPagePath(Action().RecordId), await renderer.RenderActionAsync(Action(), null, [], PublicationCatalogs.None, CancellationToken.None), "All records"),
            ("vote", PublicationProtocol.RecordPagePath(Vote().RecordId), await renderer.RenderVoteAsync(Vote(), null, [], PublicationCatalogs.None, CancellationToken.None), "All records"),
            ("relationship", PublicationProtocol.RecordPagePath(relationship.RecordId), await renderer.RenderRelationshipAsync(relationship, PublicationCatalogs.None, CancellationToken.None), "All records"),
            ("brief", PublicationProtocol.RecordPagePath(Brief().RecordId), await renderer.RenderBriefAsync(Brief(), CancellationToken.None), "Briefs"),
            ("withdrawal", PublicationProtocol.RecordPagePath(withdrawal.RecordId), await renderer.RenderWithdrawalAsync(withdrawal, CancellationToken.None), "All records"),
            ("home", PublicationProtocol.HomePath, await renderer.RenderHomeAsync(release, null, facets, NoHighlights(release), CancellationToken.None), null),
            ("all records", PublicationProtocol.AllRecordsPagePath(1), await renderer.RenderAllRecordsAsync(release, 1, CancellationToken.None), "All records"),
            ("briefs", PublicationProtocol.BriefsPagePath(1), await renderer.RenderBriefsAsync(ReleaseOfBriefs(2), ReleaseOfBriefs(2).Records, 1, CancellationToken.None), "Briefs"),
            ("issue", facets.Issues[0].PagePath(1), await renderer.RenderFacetPageAsync(facets.Issues[0], 1, CancellationToken.None), "Issues"),
            ("policy area", facets.PolicyAreas[0].PagePath(1), await renderer.RenderFacetPageAsync(facets.PolicyAreas[0], 1, CancellationToken.None), "Issues"),
            ("issues", PublicationProtocol.FacetsPath, await renderer.RenderFacetsAsync(facets, CancellationToken.None), "Issues"),
            ("official", official.PagePath, await renderer.RenderOfficialAsync(official, PublicationCatalogs.None, CancellationToken.None), "Officials"),
            ("past congress", pastOfficial.PagePath, await renderer.RenderOfficialAsync(pastOfficial, PublicationCatalogs.None, CancellationToken.None), "Officials"),
            ("officials", PublicationProtocol.OfficialsPagePath, await renderer.RenderOfficialsAsync([new OfficialListing(profile, 0)], DateTimeOffset.UnixEpoch, CancellationToken.None), "Officials"),
            ("board", OfficialBoard.All[0].PagePath, await renderer.RenderBoardAsync(OfficialBoardPage.All([], []).First(), CancellationToken.None), "Officials"),
            ("search", PublicationProtocol.SearchPagePath, await renderer.RenderSearchAsync(release, facets, CancellationToken.None), "Search"),
            ("method", PublicationProtocol.MethodPagePath, await renderer.RenderMethodAsync(CancellationToken.None), null),
            ("about", PublicationProtocol.AboutPagePath, await renderer.RenderAboutAsync(CancellationToken.None), "About"),
            ("glossary", PublicationProtocol.GlossaryPagePath, await renderer.RenderGlossaryAsync(CancellationToken.None), "Glossary"),
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
        AssertTrail(pages["board"], ["Home|index.html", "Officials|officials.html", "House Democrats"]);
        AssertTrail(pages["method"], ["Home|index.html", "How the official page counts work"]);
        AssertTrail(pages["report"], ["Home|index.html", "Report a problem"]);
        AssertTrail(pages["briefs"], ["Home|index.html", "Briefs"]);
        AssertTrail(pages["about"], ["Home|index.html", "About"]);
        AssertTrail(pages["brief"], ["Home|../index.html", "Briefs|../briefs.html", Brief().Headline]);
        AssertTrail(pages["issue"], ["Home|../index.html", "Issues|../issues.html", "Housing costs"]);
        AssertTrail(pages["vote"], ["Home|../index.html", "All records|../all.html", Vote().Headline]);
        AssertTrail(pages["withdrawal"], ["Home|../index.html", "All records|../all.html", "Withdrawn action: cccccccccccc"]);
        AssertTrail(pages["official"], ["Home|../index.html", "Officials|../officials.html", "Mayor Example"]);
        AssertTrail(pages["past congress"], ["Home|../../index.html", "Officials|../../officials.html",
            "Mayor Example|../../officials/mayor.html", "Mayor Example, the 118th Congress"]);
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

        foreach (var html in new[] { await renderer.RenderActionAsync(action, null, [], HousingCatalog, CancellationToken.None),
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

        var configured = await renderer.RenderActionAsync(Action() with { PolicyAreaId = "land-use" }, null, [], HousingCatalog, CancellationToken.None);
        AssertSingleLink(FactValue(configured, "Policy area"), "../" + PublicationProtocol.PolicyAreaPagePath("land-use", 1), "Housing and Community Development");

        var unconfigured = await renderer.RenderActionAsync(Action() with { PolicyAreaId = "mystery" }, null, [], HousingCatalog, CancellationToken.None);
        var plain = FactValue(unconfigured, "Policy area");
        Assert.Equal("Housing and Community Development", plain.ShownText().Trim());
        Assert.Empty(plain.QuerySelectorAll("a:not(.source-line)"));
    }

    private static AngleSharp.Dom.IElement FactValue(string html, params string[] labels) =>
        new HtmlParser().ParseDocument(html).QuerySelectorAll(".facts dt").Single(term => labels.Contains(term.TextContent)).NextElementSibling!;

    private static void AssertSingleLink(AngleSharp.Dom.IElement scope, string href, string text)
    {
        var link = Assert.Single(scope.QuerySelectorAll("a:not(.source-line)"));
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
            ["briefs"] = 1,
            ["home"] = 3,
            ["issue"] = 1,
            ["issues"] = 2,
            ["official"] = 3,
            ["policy area"] = 1,
            ["record"] = 1,
            ["statement"] = 1,
            ["vote"] = 1
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
    public void TheSortStatusLineTemplateEndsWithTheNoticeThatSortedRowsLeaveOutTheBadgeAndTheCounts()
    {
        var script = System.Text.Encoding.UTF8.GetString(new ReleaseRenderer(null, null).Assets.Single(asset => asset.RelativePath == "assets/js/public.js").Content);

        Assert.Contains("sorted by ${label(current.header, current.direction)}. Sorted rows leave out the party badge and the vote counts.", script, StringComparison.Ordinal);
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
        var html = await new ReleaseRenderer(null, null).RenderHomeAsync(ReleaseOf(1), Brief(), NoFacets, NoHighlights(ReleaseOf(1)), CancellationToken.None);

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
    private static HomeHighlights NoHighlights(PublicationRelease release) => HomeHighlights.From(release, []);

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

    private static PublicationRelease ReleaseOfBriefs(int count) => ReleaseOf(0) with
    {
        Records = [.. Enumerable.Range(0, count).Select(index => new PublishedRecordEntry
        {
            IssueIds = [],
            Kind = PublishedRecordKind.Brief,
            Officials = [],
            PolicyAreaIds = [],
            RecordId = index.ToString("x64"),
            RevisionNumber = 1,
            Headline = $"Brief {index}",
            EventDate = new DateOnly(2026, 1, 5).AddDays(7 * index),
            FirstPublishedAtUtc = new DateTimeOffset(2026, 1, 12, 8, 0, 0, TimeSpan.Zero).AddDays(7 * index)
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
            Source = PublishedVoteSource.CongressGov,
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

    private static PublishedVote VoteWithParties(PublishedPartyMajority[] majorities, params (string Name, string Party, string Cast)[] voters)
    {
        var template = VoteWithCasts([.. voters.Select(voter => (voter.Name, voter.Cast))]);
        return template with
        {
            PartyMajorities = majorities,
            Totals = new() { Yea = majorities.Sum(party => party.YeaCount), Nay = majorities.Sum(party => party.NayCount), Present = 0, NotVoting = 0 },
            Members = [.. template.Members.Select((member, index) => member with { Party = voters[index].Party })]
        };
    }

    private static PublishedPartyMajority[] DemocratsYeaRepublicansNay =>
        [PublishedPartyMajority.Of(PublishedPartyMajority.Democrat, 2, 1), PublishedPartyMajority.Of(PublishedPartyMajority.Republican, 1, 3)];

    private static async Task<IHtmlDocument> RenderedAsync(PublishedVote vote) =>
        new HtmlParser().ParseDocument(await new ReleaseRenderer(null, null).RenderVoteAsync(vote, null, [], PublicationCatalogs.None, CancellationToken.None));

    private static PublishedVote VoteWithCasts(IReadOnlyList<(string Name, string Cast)> voters)
    {
        var template = Vote();
        var source = template.Sources[1];
        var lines = voters.Select((voter, index) => $"Member: B{index:D6}, {voter.Name}, R, AL, {voter.Cast}").ToArray();
        var text = string.Concat(lines.Select(line => line + "\n"));
        return template with
        {
            Sources = [template.Sources[0], source with { Text = text }],
            Members = [.. lines.Select((line, index) => new PublishedMemberVote
            {
                Official = new PublishedOfficial { Id = $"o{index}", Name = voters[index].Name },
                Party = "Republican",
                Cast = voters[index].Cast,
                Citation = new PublishedCitation { ExtractionId = source.ExtractionId, Start = text.IndexOf(line, StringComparison.Ordinal), Length = line.Length, Quote = line }
            })]
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
            TextOrigin = PublishedTextOrigin.Person,
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
            ApprovedBy = PublishedApprover.Person,
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
