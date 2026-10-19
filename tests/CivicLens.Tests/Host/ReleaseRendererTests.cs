using System.Text.RegularExpressions;
using AngleSharp.Html.Parser;
using CivicLens.Host.Publication;
using CivicLens.Publication.Contracts;

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
        var html = await new ReleaseRenderer().RenderRecordAsync(Record(), CancellationToken.None);

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
        var html = await new ReleaseRenderer().RenderRecordAsync(Record(), CancellationToken.None);
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
    public async Task FullVersionsKeepALeadingNewlineBecauseThePageMustMatchTheApprovedEvidence()
    {
        var record = Record("\n" + BeforeText, "\n" + AfterText);

        var html = await new ReleaseRenderer().RenderRecordAsync(record, CancellationToken.None);

        var shown = new HtmlParser().ParseDocument(html).QuerySelectorAll(".doc-text").Select(text => text.TextContent);
        Assert.Equal([record.Before.Text, record.After.Text], shown);
    }

    [Fact]
    public async Task RecordPageIsPublicStaticAndRelative()
    {
        var html = await new ReleaseRenderer().RenderRecordAsync(Record(), CancellationToken.None);

        Assert.StartsWith("<!DOCTYPE html>", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<html lang=\"en\">", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Private", html, StringComparison.Ordinal);
        Assert.DoesNotContain("not published", html, StringComparison.Ordinal);
        Assert.Contains("href=\"../assets/css/theme.css\"", html, StringComparison.Ordinal);
        Assert.Contains("src=\"../assets/js/app.js\"", html, StringComparison.Ordinal);
        Assert.Contains("href=\"../index.html\"", html, StringComparison.Ordinal);
        Assert.DoesNotMatch(new Regex("(?:href|src)=\"(?:/|//)"), html);
        Assert.Contains("rel=\"noopener noreferrer nofollow\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RecordPageKeepsObservationDatesApartFromTheChangeDate()
    {
        var withDate = await new ReleaseRenderer().RenderRecordAsync(Record(), CancellationToken.None);
        var withoutDate = await new ReleaseRenderer().RenderRecordAsync(
            Record() with { ChangeDate = null, ChangeDateEvidence = null }, CancellationToken.None);

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
        var html = await new ReleaseRenderer().RenderRecordAsync(Record(), CancellationToken.None);

        Assert.Contains("Changed passage 1", html, StringComparison.Ordinal);
        Assert.Contains("hidden", html, StringComparison.Ordinal);
        Assert.DoesNotContain("/Documents/Citation", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task IndexPageSearchReadsTheReleaseManifestAndStaysHiddenUntilScriptRuns()
    {
        var html = await new ReleaseRenderer().RenderIndexAsync(ReleaseOf(1), 1, CancellationToken.None);
        var document = new HtmlParser().ParseDocument(html);

        var form = document.QuerySelector("form[data-release-search]")!;
        Assert.Equal(PublicationProtocol.ManifestPath, form.GetAttribute("data-release-search"));
        Assert.Equal(PublicationProtocol.RecordPagePath("{id}"), form.GetAttribute("data-record-page"));
        Assert.True(form.Closest("section")!.HasAttribute("hidden"));
        Assert.Equal(["", "documentChange", "statement", "action", "brief", "vote", "relationship"],
            document.QuerySelectorAll("#search-kind option").Select(option => option.GetAttribute("value")));
        Assert.Equal(["text", "kind", "official", "issue", "policyArea", "from", "to"],
            form.QuerySelectorAll("input, select").Select(control => control.GetAttribute("name")));
    }

    [Fact]
    public async Task IndexPageListsRecordsWithRelativeLinks()
    {
        var release = new PublicationRelease
        {
            SchemaVersion = PublicationProtocol.SchemaVersion,
            ReleaseNumber = 4,
            PublishedAtUtc = new DateTimeOffset(2026, 5, 11, 8, 0, 0, TimeSpan.Zero),
            Records =
            [
                new PublishedRecordEntry
                {
                    RecordId = new string('c', 32),
                    RevisionNumber = 1,
                    Headline = "Headline <i>one</i>",
                    FirstPublishedAtUtc = new DateTimeOffset(2026, 5, 11, 8, 0, 0, TimeSpan.Zero)
                }
            ]
        };

        var html = await new ReleaseRenderer().RenderIndexAsync(release, 1, CancellationToken.None);

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
        var html = await new ReleaseRenderer().RenderStatementAsync(Statement(), [], CancellationToken.None);

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
    public async Task StatementPageOmitsTheDateFactAndItsEvidenceWhenTheStatementHasNoEstablishedDate()
    {
        var html = await new ReleaseRenderer().RenderStatementAsync(
            Statement() with { StatementDate = null, StatementDateEvidence = null }, [], CancellationToken.None);

        Assert.DoesNotContain("Statement date", html, StringComparison.Ordinal);
        Assert.DoesNotContain("cite-statement-date", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StatementPageRejectsAQuoteThatIsNotTheCitedSourceText()
    {
        var statement = Statement() with { Quotations = [Statement().Quotations[0] with { Quote = "invented words" }] };

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new ReleaseRenderer().RenderStatementAsync(statement, [], CancellationToken.None));
    }

    [Fact]
    public async Task ActionPageShowsTheBillTheSponsorshipTableTheCrsSummaryAndTheSources()
    {
        var html = await new ReleaseRenderer().RenderActionAsync(Action(), [], CancellationToken.None);

        var document = new HtmlParser().ParseDocument(html);
        var facts = document.QuerySelectorAll(".facts dt").Zip(document.QuerySelectorAll(".facts dd"))
            .ToDictionary(pair => pair.First.TextContent, pair => pair.Second.TextContent);
        Assert.Equal("HR 7, Congress 119", facts["Bill"]);
        Assert.Equal("A <housing> bill", facts["Title"]);
        Assert.Equal("Housing and Community Development", facts["Policy area"]);
        Assert.Equal("housing", facts["Tracked issues"]);
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
        var renderer = new ReleaseRenderer();

        foreach (var html in new[] { await renderer.RenderActionAsync(Action(), [], CancellationToken.None),
                     await renderer.RenderVoteAsync(Vote(), null, [], CancellationToken.None) })
        {
            Assert.Contains("From the official record, checked", html, StringComparison.Ordinal);
            Assert.DoesNotContain("Approved 20", html, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task ActionAndVotePagesLabelAnAiSummaryOnlyWhenAModelWroteTheText()
    {
        var renderer = new ReleaseRenderer();
        var action = Action();
        var vote = Vote();
        var person = PublishedTextOrigin.Person;

        var model = new[] { await renderer.RenderActionAsync(action, [], CancellationToken.None),
            await renderer.RenderVoteAsync(vote, null, [], CancellationToken.None) };
        var written = new[] { await renderer.RenderActionAsync(action with { TextOrigin = person }, [], CancellationToken.None),
            await renderer.RenderVoteAsync(vote with { TextOrigin = person }, null, [], CancellationToken.None) };

        Assert.All(model, html => Assert.Contains("An AI summary of the official record", html, StringComparison.Ordinal));
        Assert.All(written, html => Assert.DoesNotContain("AI summary", html, StringComparison.Ordinal));
    }

    [Fact]
    public async Task EveryActionQuoteLinksToTheMarkOfTheSameTextInTheSourceAndEachSourceIsLabelledByPosition()
    {
        var html = await new ReleaseRenderer().RenderActionAsync(Action(), [], CancellationToken.None);

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

        await Assert.ThrowsAsync<InvalidDataException>(() => new ReleaseRenderer().RenderActionAsync(action, [], CancellationToken.None));
    }

    [Fact]
    public async Task VotePageShowsTheDayTheKindTheResultFactsTheCastOfEachTrackedMemberAndTheSources()
    {
        var html = await new ReleaseRenderer().RenderVoteAsync(Vote(), null, [], CancellationToken.None);

        var document = new HtmlParser().ParseDocument(html);
        var facts = document.QuerySelectorAll(".facts dt").Zip(document.QuerySelectorAll(".facts dd"))
            .ToDictionary(pair => pair.First.TextContent, pair => pair.Second.TextContent);
        Assert.Equal("2025-04-10", facts["Voted on"]);
        Assert.Equal("Passage", facts["Kind"]);
        Assert.Equal("100, Congress 119, session 1", facts["Roll call"]);
        Assert.Equal("housing", facts["Tracked issues"]);
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

    [Fact]
    public async Task EveryVoteQuoteLinksToTheMarkOfTheSameTextInTheSource()
    {
        var html = await new ReleaseRenderer().RenderVoteAsync(Vote(), null, [], CancellationToken.None);

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

        var linked = new HtmlParser().ParseDocument(await new ReleaseRenderer().RenderVoteAsync(Vote(), actionId, [], CancellationToken.None));
        var plain = new HtmlParser().ParseDocument(await new ReleaseRenderer().RenderVoteAsync(Vote(), null, [], CancellationToken.None));

        var link = Assert.Single(linked.QuerySelectorAll("[aria-labelledby='bill-title'] a"));
        Assert.Equal(($"../records/{actionId}.html", "HR 7, Congress 119"), (link.GetAttribute("href"), link.TextContent));
        Assert.Empty(plain.QuerySelectorAll("[aria-labelledby='bill-title'] a"));
        Assert.Contains("HR 7, Congress 119", plain.QuerySelector("[aria-labelledby='bill-title']")!.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AVoteWithoutABillShowsNoBillPanel()
    {
        var html = await new ReleaseRenderer().RenderVoteAsync(Vote() with { BillType = null, BillNumber = null }, null, [], CancellationToken.None);

        Assert.Empty(new HtmlParser().ParseDocument(html).QuerySelectorAll("[aria-labelledby='bill-title']"));
    }

    [Fact]
    public async Task VotePageRejectsACitationThatNamesNoEmbeddedSource()
    {
        var vote = Vote() with { VotedOnCitation = Vote().VotedOnCitation with { ExtractionId = new string('9', 64) } };

        await Assert.ThrowsAsync<InvalidDataException>(() => new ReleaseRenderer().RenderVoteAsync(vote, null, [], CancellationToken.None));
    }

    [Fact]
    public async Task EachKindOfCitedRecordListsItsRelatedRecordsAsLinksWithTheLinkTypeAndTheStatementDate()
    {
        var statement = Statement();
        var action = Action();
        var vote = Vote() with { RecordId = new string('9', 64) };
        var actionLink = Relationship(PublishedRecordKind.Action, action.RecordId, '7');
        var voteLink = Relationship(PublishedRecordKind.Vote, vote.RecordId, '8') with { Type = PublishedRelationshipType.StatementAboutVote };
        var renderer = new ReleaseRenderer();

        var statementPage = new HtmlParser().ParseDocument(await renderer.RenderStatementAsync(statement, [actionLink, voteLink], CancellationToken.None));
        var actionPage = new HtmlParser().ParseDocument(await renderer.RenderActionAsync(action, [actionLink], CancellationToken.None));
        var votePage = new HtmlParser().ParseDocument(await renderer.RenderVoteAsync(vote, null, [voteLink], CancellationToken.None));

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
        var renderer = new ReleaseRenderer();

        Assert.Empty(new HtmlParser().ParseDocument(await renderer.RenderStatementAsync(Statement(), [], CancellationToken.None))
            .QuerySelectorAll("[aria-labelledby='related-title']"));
        Assert.Empty(new HtmlParser().ParseDocument(await renderer.RenderActionAsync(Action(), [], CancellationToken.None))
            .QuerySelectorAll("[aria-labelledby='related-title']"));
        Assert.Empty(new HtmlParser().ParseDocument(await renderer.RenderVoteAsync(Vote(), null, [], CancellationToken.None))
            .QuerySelectorAll("[aria-labelledby='related-title']"));
    }

    [Fact]
    public async Task RelationshipPageNamesBothCitedRevisionsLinksTheirPagesEncodesTheApprovedEvidenceAndStatesNoVerdict()
    {
        var link = Relationship(PublishedRecordKind.Action, new string('a', 64), '7');

        var html = await new ReleaseRenderer().RenderRelationshipAsync(link, CancellationToken.None);

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

        await Assert.ThrowsAsync<InvalidDataException>(() => new ReleaseRenderer().RenderRelationshipAsync(link, CancellationToken.None));
    }

    [Fact]
    public async Task BriefPageShowsEachCitationAsALinkToItsRecordPageWithTheCopiedHeadlineTheWeekTheCutoffAndTheSampleNote()
    {
        var html = await new ReleaseRenderer().RenderBriefAsync(Brief(), CancellationToken.None);

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

        await Assert.ThrowsAsync<InvalidDataException>(() => new ReleaseRenderer().RenderBriefAsync(
            brief with { Paragraphs = [.. brief.Paragraphs, new() { Text = "More.", CitationIds = ["R9"] }] }, CancellationToken.None));
    }

    [Fact]
    public async Task IndexPageLabelsEachEntryWithItsKindSoReadersKnowWhatTheyOpen()
    {
        var release = ReleaseOf(2);
        release = release with
        {
            Records = [release.Records[0], release.Records[1] with { Kind = PublishedRecordKind.Statement, RecordId = new string('d', 64) },
                release.Records[1] with { Kind = PublishedRecordKind.Action, RecordId = new string('f', 64) }]
        };

        var html = await new ReleaseRenderer().RenderIndexAsync(release, 1, CancellationToken.None);

        var rows = new HtmlParser().ParseDocument(html).QuerySelectorAll("tbody tr")
            .Select(row => (row.QuerySelector("a")!.GetAttribute("href"), row.QuerySelector(".badge")!.TextContent));
        Assert.Equal([($"records/{release.Records[0].RecordId}.html", "Document change"),
            ($"records/{new string('d', 64)}.html", "Statement"), ($"records/{new string('f', 64)}.html", "Action")], rows);
    }

    [Theory]
    [InlineData(1, 50, 0, "Page 1 of 3", false, "page-2.html")]
    [InlineData(2, 50, 50, "Page 2 of 3", true, "page-3.html")]
    [InlineData(3, 20, 100, "Page 3 of 3", true, null)]
    public async Task IndexPageShowsOnlyItsSliceInManifestOrderWithPagerLinksWhereTheyApply(
        int page, int expectedCount, int firstIndex, string label, bool hasPrevious, string? next)
    {
        var release = ReleaseOf(120);

        var html = await new ReleaseRenderer().RenderIndexAsync(release, page, CancellationToken.None);

        var shown = Regex.Matches(html, "href=\"records/([0-9a-f]{32})\\.html\"").Select(match => match.Groups[1].Value).ToList();
        Assert.Equal(release.Records.Skip(firstIndex).Take(expectedCount).Select(entry => entry.RecordId), shown);
        Assert.Contains(label, html, StringComparison.Ordinal);
        var document = new HtmlParser().ParseDocument(html);
        var previous = page == 2 ? "index.html" : "page-2.html";
        Assert.Equal(hasPrevious ? previous : null, document.QuerySelector("a[rel='prev']")?.GetAttribute("href"));
        Assert.Equal(next, document.QuerySelector("a[rel='next']")?.GetAttribute("href"));
        Assert.Contains("href=\"assets/css/theme.css\"", html, StringComparison.Ordinal);
        Assert.DoesNotMatch(new Regex("(?:href|src)=\"(?:/|//|https?:)"), html);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    public async Task IndexPageRejectsAPageOutsideTheRelease(int page)
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            new ReleaseRenderer().RenderIndexAsync(ReleaseOf(120), page, CancellationToken.None));
    }

    [Fact]
    public void AssetsIncludeTheThemeAtItsReleaseRelativePath()
    {
        var assets = new ReleaseRenderer().Assets;

        foreach (var path in new[] { "assets/css/theme.css", "assets/css/app.css", "assets/js/theme.js", "assets/js/app.js",
                     "assets/fonts/instrument-sans.woff2", "assets/fonts/newsreader.woff2" })
            Assert.Contains(assets, asset => asset.RelativePath == path && asset.Content.Length > 0);
    }

    private static PublicationRelease ReleaseOf(int count) => new()
    {
        SchemaVersion = PublicationProtocol.SchemaVersion,
        ReleaseNumber = 4,
        PublishedAtUtc = new DateTimeOffset(2026, 5, 11, 8, 0, 0, TimeSpan.Zero),
        Records = [.. Enumerable.Range(0, count).Select(index => new PublishedRecordEntry
        {
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
        Officials = [new PublishedOfficial { Id = "o1", Name = "Jane <Doe>" }],
        IssueIds = ["housing"],
        PolicyAreaIds = [],
        ApprovedAtUtc = DateTimeOffset.UnixEpoch,
        FirstPublishedAtUtc = DateTimeOffset.UnixEpoch
    };

    private static PublishedBrief Brief() => new()
    {
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
        ApprovedAtUtc = DateTimeOffset.UnixEpoch,
        FirstPublishedAtUtc = DateTimeOffset.UnixEpoch
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

    private static PublishedVote Vote()
    {
        const string detail = "Started: 2025-04-10T11:08:00-04:00\nQuestion: On Passage\nResult: Passed <b>\n";
        const string members = "Member: A000055, Jane Doe, R, AL, Yea\n";
        var detailId = new string('1', 64);
        var membersId = new string('2', 64);
        PublishedCitation Cite(string id, string text, string quote) =>
            new() { ExtractionId = id, Start = text.IndexOf(quote, StringComparison.Ordinal), Length = quote.Length, Quote = quote };
        PublishedDocumentVersion Source(string id, string url, string text) =>
            new() { ExtractionId = id, Url = url, ObservedAtUtc = new DateTimeOffset(2026, 1, 21, 9, 0, 0, TimeSpan.Zero), Text = text };
        return new PublishedVote
        {
            RecordId = new string('f', 64),
            RevisionNumber = 1,
            Headline = "House passes a housing bill",
            Summary = "The House voted.",
            TextOrigin = PublishedTextOrigin.Model,
            Congress = 119,
            Session = 1,
            RollCallNumber = 100,
            Kind = PublishedVoteKind.Passage,
            KindCitation = Cite(detailId, detail, "Question: On Passage"),
            VotedOn = new DateOnly(2025, 4, 10),
            VotedOnCitation = Cite(detailId, detail, "Started: 2025-04-10T11:08:00-04:00"),
            Facts =
            [
                new PublishedVoteFact { Text = "Started: 2025-04-10T11:08:00-04:00", Citation = Cite(detailId, detail, "Started: 2025-04-10T11:08:00-04:00") },
                new PublishedVoteFact { Text = "Question: On Passage", Citation = Cite(detailId, detail, "Question: On Passage") },
                new PublishedVoteFact { Text = "Result: Passed <b>", Citation = Cite(detailId, detail, "Result: Passed <b>") }
            ],
            Members =
            [
                new PublishedMemberVote
                {
                    Official = new PublishedOfficial { Id = "o1", Name = "Jane <Doe>" },
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
