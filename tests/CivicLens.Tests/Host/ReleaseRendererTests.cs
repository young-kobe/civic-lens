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

        var shown = new HtmlParser().ParseDocument(html).QuerySelectorAll("pre.public-text").Select(pre => pre.TextContent);
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
        Assert.Contains("href=\"../assets/theme.css\"", html, StringComparison.Ordinal);
        Assert.Contains("src=\"../assets/diff.js\"", html, StringComparison.Ordinal);
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
        Assert.Contains("href=\"assets/theme.css\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Private", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Page 1 of", html, StringComparison.Ordinal);
        Assert.DoesNotContain("rel=\"prev\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("rel=\"next\"", html, StringComparison.Ordinal);
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
        Assert.Equal(hasPrevious, html.Contains("rel=\"prev\"", StringComparison.Ordinal));
        if (hasPrevious)
            Assert.Contains(page == 2 ? "rel=\"prev\" href=\"index.html\"" : "rel=\"prev\" href=\"page-2.html\"", html, StringComparison.Ordinal);
        Assert.Equal(next is not null, html.Contains("rel=\"next\"", StringComparison.Ordinal));
        if (next is not null) Assert.Contains($"rel=\"next\" href=\"{next}\"", html, StringComparison.Ordinal);
        Assert.Contains("href=\"assets/theme.css\"", html, StringComparison.Ordinal);
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

        Assert.Contains(assets, asset => asset.RelativePath == "assets/theme.css" && asset.Content.Length > 0);
        Assert.Contains(assets, asset => asset.RelativePath == "assets/diff.js" && asset.Content.Length > 0);
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
