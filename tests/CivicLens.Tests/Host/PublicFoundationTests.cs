using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using AngleSharp.Html.Parser;
using CivicLens.Application.Publication;
using CivicLens.Host.Components.Ui;
using CivicLens.Host.Publication;
using CivicLens.Publication.Contracts;
using CivicLens.Tests.Fixtures;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace CivicLens.Tests.Host;

public sealed class PublicFoundationTests
{
    [Fact]
    public async Task TheHeaderHoldsThreeSectionsAndASearchFormAndTheFooterHoldsTheOtherPagesSoThePhoneHeaderStaysShort()
    {
        var document = new HtmlParser().ParseDocument(await new ReleaseRenderer(null, null).RenderMethodAsync(CancellationToken.None));

        Assert.Equal(["Officials", "Issues", "Votes and bills"], document.QuerySelectorAll("nav[aria-label='Site'] a").Select(link => link.TextContent));
        Assert.Equal(["Weekly briefs", "How the counts work", "Glossary", "About", "Report a problem"],
            document.QuerySelectorAll("nav[aria-label='Footer'] a").Select(link => link.TextContent));
        var form = document.QuerySelector("header form[role='search']")!;
        Assert.Equal("search.html", form.GetAttribute("action"));
        Assert.Equal("q", form.QuerySelector("input[type='search']")!.GetAttribute("name"));
        Assert.Equal("Search records", form.QuerySelector("label[for='site-search']")!.TextContent);
        Assert.NotNull(document.QuerySelector("a.skip[href='#main']"));
        Assert.NotNull(document.QuerySelector(".theme-toggle"));
    }

    [Fact]
    public async Task EveryNamedIconDrawsShapesAndAnIssueWithoutAnIconDrawsNothingSoTheListStaysClosed()
    {
        foreach (var name in Enum.GetValues<IconName>())
            Assert.NotEmpty(new HtmlParser().ParseFragment(await RenderAsync<Icon>(new() { [nameof(Icon.Name)] = name }), null!)
                .OfType<AngleSharp.Dom.IElement>().Single().QuerySelectorAll("path, circle, rect"));

        Assert.Equal("", await RenderAsync<Icon>(new() { [nameof(Icon.IssueId)] = "an-issue-added-later" }));
        Assert.Contains("<path", await RenderAsync<Icon>(new() { [nameof(Icon.IssueId)] = "firearms" }), StringComparison.Ordinal);
    }

    [Fact]
    public void EveryConfiguredIssueHasOneIconAndNoIconBelongsToAnUnknownIssue()
    {
        using var config = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepositoryRoot.FullName, "config", "issues.json")));
        var ids = config.RootElement.GetProperty("issues").EnumerateArray().Select(issue => issue.GetProperty("id").GetString()!);

        Assert.Equal(ids.Order(StringComparer.Ordinal), IssueIcons.Paths.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task AnInfoPopoverHasAnAccessibleNameAndOpensTheSamePopoverASingleTermUses()
    {
        var html = await RenderAsync<InfoPopover>(new()
        {
            [nameof(InfoPopover.Id)] = "note-1",
            [nameof(InfoPopover.Label)] = "What this number counts",
            [nameof(InfoPopover.ChildContent)] = (RenderFragment)(builder => builder.AddContent(0, "Votes cast in this Congress."))
        });

        var document = new HtmlParser().ParseDocument(html);
        var button = document.QuerySelector("button")!;
        Assert.Equal("What this number counts", button.GetAttribute("aria-label"));
        Assert.Equal("note-1", button.GetAttribute("popovertarget"));
        var popover = document.QuerySelector("#note-1")!;
        Assert.Equal("term-popover", popover.ClassName);
        Assert.True(popover.HasAttribute("popover"));
    }

    [Fact]
    public async Task APagedTableKeepsEveryRowInItsHtmlSoNoRowNeedsJavaScriptToBeReached()
    {
        var votes = Enumerable.Range(0, 40).Select(index => Vote(index)).ToArray();
        var page = new ReleaseOfficialPage(new PublishedOfficialProfile { Id = "doe", Name = "Jane Doe", Terms = [] }, true, 119, true, [119], [], [.. votes], DateTimeOffset.UnixEpoch);

        var document = new HtmlParser().ParseDocument(await new ReleaseRenderer(null, null).RenderOfficialAsync(page, PublicationCatalogs.None, CancellationToken.None));

        var table = document.QuerySelector("table[data-paged]")!;
        Assert.Equal(DataTable<int>.PagedRowCount.ToString(CultureInfo.InvariantCulture), table.GetAttribute("data-paged"));
        Assert.Equal("records", table.GetAttribute("data-rows-noun"));
        Assert.Equal(40, table.QuerySelectorAll("tbody tr").Length);
        Assert.Empty(table.QuerySelectorAll("tbody tr[hidden]"));
        Assert.Null(document.QuerySelector("nav.pager"));
    }

    [Fact]
    public void ThePublicTokensReachFourAndAHalfToOneInBothThemes()
    {
        var theme = File.ReadAllText(Path.Combine(RepositoryRoot.FullName, "src", "CivicLens.Host", "wwwroot", "css", "theme.css"));
        var block = theme[theme.IndexOf("body.public {", StringComparison.Ordinal)..];
        block = block[..block.IndexOf('}', StringComparison.Ordinal)];
        var tokens = Regex.Matches(block, @"(--[a-z0-9-]+):\s*([^;]+);").ToDictionary(match => match.Groups[1].Value, match => match.Groups[2].Value);
        string[] textOnSurfaces = ["--ink", "--ink-2", "--muted", "--info"];
        string[] surfaces = ["--ground", "--surface", "--raised", "--info-bg"];

        foreach (var theme2 in new[] { 0, 1 })
        {
            string Value(string name) => Regex.IsMatch(tokens[name], "^light-dark")
                ? Regex.Match(tokens[name], @"^light-dark\((.+?),\s*(?=(?:#|rgba))(.+)\)$").Groups[theme2 + 1].Value
                : tokens[name];
            var ground = Color.Parse(Value("--ground"), null);
            foreach (var text in textOnSurfaces)
                foreach (var surface in surfaces.Where(surface => !(text != "--info" && surface == "--info-bg")))
                    Assert.True(Color.Parse(Value(text), ground).ContrastWith(Color.Parse(Value(surface), ground)) >= 4.5, $"{text} on {surface}, theme {theme2}");
            Assert.True(Color.Parse(Value("--primary-ink"), ground).ContrastWith(Color.Parse(Value("--primary"), ground)) >= 4.5, $"button, theme {theme2}");
            Assert.True(Color.Parse(Value("--bar-ink"), ground).ContrastWith(Color.Parse(Value("--bar"), ground)) >= 4.5, $"header, theme {theme2}");
            Assert.True(Color.Parse(Value("--bar-muted"), ground).ContrastWith(Color.Parse(Value("--bar"), ground)) >= 4.5, $"header muted, theme {theme2}");
        }
    }

    private static PublishedOfficialVote Vote(int index) => new()
    {
        RecordId = new string('a', 32) + index.ToString("D32", CultureInfo.InvariantCulture),
        VotedOn = new DateOnly(2026, 4, 1).AddDays(-index),
        Chamber = PublishedChamber.House,
        Congress = 119,
        Session = 1,
        RollCallNumber = 100 + index,
        BillType = "hr",
        BillNumber = 7,
        Cast = "Yea",
        Result = "Passed",
        Totals = new() { Yea = 218, Nay = 210, Present = 1, NotVoting = 5 },
        SplitVote = false,
        Party = "Republican",
        Headline = $"Vote {index}",
        Kind = PublishedVoteKind.Passage,
        IssueIds = []
    };

    private static async Task<string> RenderAsync<TComponent>(Dictionary<string, object?> parameters) where TComponent : IComponent
    {
        await using var renderer = new HtmlRenderer(new ServiceCollection().BuildServiceProvider(), NullLoggerFactory.Instance);
        return await renderer.Dispatcher.InvokeAsync(async () =>
            (await renderer.RenderComponentAsync<TComponent>(ParameterView.FromDictionary(parameters))).ToHtmlString().Trim());
    }

    private readonly record struct Color(double R, double G, double B)
    {
        public static Color Parse(string text, Color? under)
        {
            var hex = Regex.Match(text, "#([0-9a-f]{6})");
            if (hex.Success)
                return new(Convert.ToInt32(hex.Groups[1].Value[..2], 16), Convert.ToInt32(hex.Groups[1].Value[2..4], 16), Convert.ToInt32(hex.Groups[1].Value[4..], 16));
            var parts = Regex.Match(text, @"rgba\((\d+),\s*(\d+),\s*(\d+),\s*([\d.]+)\)").Groups;
            var alpha = double.Parse(parts[4].Value, CultureInfo.InvariantCulture);
            var back = under!.Value;
            double Mix(string value, double below) => alpha * int.Parse(value, CultureInfo.InvariantCulture) + (1 - alpha) * below;
            return new(Mix(parts[1].Value, back.R), Mix(parts[2].Value, back.G), Mix(parts[3].Value, back.B));
        }

        public double ContrastWith(Color other)
        {
            var (high, low) = Luminance >= other.Luminance ? (Luminance, other.Luminance) : (other.Luminance, Luminance);
            return (high + .05) / (low + .05);
        }

        private double Luminance => .2126 * Linear(R) + .7152 * Linear(G) + .0722 * Linear(B);

        private static double Linear(double channel)
        {
            var c = channel / 255;
            return c <= .03928 ? c / 12.92 : Math.Pow((c + .055) / 1.055, 2.4);
        }
    }
}
