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
        Assert.Equal(["", "documentChange", "statement", "action"],
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
        var html = await new ReleaseRenderer().RenderStatementAsync(Statement(), CancellationToken.None);

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
            Statement() with { StatementDate = null, StatementDateEvidence = null }, CancellationToken.None);

        Assert.DoesNotContain("Statement date", html, StringComparison.Ordinal);
        Assert.DoesNotContain("cite-statement-date", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StatementPageRejectsAQuoteThatIsNotTheCitedSourceText()
    {
        var statement = Statement() with { Quotations = [Statement().Quotations[0] with { Quote = "invented words" }] };

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new ReleaseRenderer().RenderStatementAsync(statement, CancellationToken.None));
    }

    [Fact]
    public async Task ActionPageShowsTheBillTheSponsorshipTableTheCrsSummaryAndTheSources()
    {
        var html = await new ReleaseRenderer().RenderActionAsync(Action(), CancellationToken.None);

        var document = new HtmlParser().ParseDocument(html);
        var facts = document.QuerySelectorAll(".facts dt").Zip(document.QuerySelectorAll(".facts dd"))
            .ToDictionary(pair => pair.First.TextContent, pair => pair.Second.TextContent);
        Assert.Equal("HR 7, Congress 119", facts["Bill"]);
        Assert.Equal("A <housing> bill", facts["Title"]);
        Assert.Equal("Housing and Community Development", facts["Policy area"]);
        Assert.Equal("housing", facts["Tracked issues"]);
        var rows = document.QuerySelectorAll("[aria-labelledby='sponsorships-title'] tbody tr")
            .Select(row => string.Join("|", row.QuerySelectorAll("td").Select(cell => cell.TextContent.Trim())));
        Assert.Equal(["Jane <Doe>|Sponsor|No|2026-01-03|None|Sponsor: D000001", "Ann Roe|Cosponsor|Yes|2026-01-05|2026-01-20|Cosponsor: R000002, original"], rows);
        Assert.Contains("Summary version: 00 <i>funds</i> rent aid.", document.QuerySelector("[aria-labelledby='bill-evidence-title']")!.TextContent,
            StringComparison.Ordinal);
        Assert.Contains("href=\"https://api.congress.gov/v3/bill/119/hr/7/cosponsors\"", html, StringComparison.Ordinal);
        Assert.Contains("A sponsorship is a proposal, not a law.", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<i>", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ActionPageRejectsACitationThatNamesNoEmbeddedSource()
    {
        var action = Action() with { SummaryCitation = Action().SummaryCitation! with { ExtractionId = new string('9', 64) } };

        await Assert.ThrowsAsync<InvalidDataException>(() => new ReleaseRenderer().RenderActionAsync(action, CancellationToken.None));
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
