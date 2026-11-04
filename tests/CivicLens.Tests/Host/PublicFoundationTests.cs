using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using AngleSharp.Html.Parser;
using CivicLens.Application.Issues;
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
    public async Task TheHeaderHoldsThreeSectionsAndTheFooterHoldsTheOtherPagesSoThePhoneHeaderStaysShort()
    {
        var document = new HtmlParser().ParseDocument(await new ReleaseRenderer(null, null).RenderMethodAsync(CancellationToken.None));

        Assert.Equal(["Officials", "Issues", "Votes and bills"], document.QuerySelectorAll("nav[aria-label='Site'] a").Select(link => link.TextContent));
        Assert.Equal(["How we check records", "Glossary", "About", "Report a problem"],
            document.QuerySelectorAll("nav[aria-label='Footer'] a").Select(link => link.TextContent));
        Assert.Equal("Civic Lens", document.QuerySelector("footer .public-footer-name")!.TextContent);
        Assert.NotNull(document.QuerySelector("a.skip[href='#main']"));
    }

    [Fact]
    public async Task TheThemeMenuOffersLightSystemAndDarkAndKeepsTheStoredChoiceKeys()
    {
        var document = new HtmlParser().ParseDocument(await new ReleaseRenderer(null, null).RenderMethodAsync(CancellationToken.None));

        var menu = document.QuerySelector("header .theme-menu")!;
        Assert.Equal("theme-menu", menu.QuerySelector("button[popovertarget]")!.GetAttribute("popovertarget"));
        Assert.Equal(["Light", "Match system", "Dark"], menu.QuerySelectorAll("#theme-menu [data-theme-choice]").Select(button => button.TextContent));
        Assert.Equal(["light", "", "dark"], menu.QuerySelectorAll("#theme-menu [data-theme-choice]").Select(button => button.GetAttribute("data-theme-choice")));
        Assert.Null(document.QuerySelector(".theme-toggle"));
    }

    [Fact]
    public async Task EveryNamedIconDrawsShapesAndAnIssueWithoutAnIconDrawsTheNeutralDefaultSoNoTileIsEmpty()
    {
        foreach (var name in Enum.GetValues<IconName>())
            Assert.NotEmpty(new HtmlParser().ParseFragment(await RenderAsync<Icon>(new() { [nameof(Icon.Name)] = name }), null!)
                .OfType<AngleSharp.Dom.IElement>().Single().QuerySelectorAll("path, circle, rect"));

        foreach (var name in IssueIcons.Paths.Keys)
            Assert.Contains(IssueIcons.Paths[name], await RenderAsync<IssueIcon>(new() { [nameof(IssueIcon.Name)] = name }), StringComparison.Ordinal);
        Assert.Contains(IssueIcons.Paths[IssueIcons.Default], await RenderAsync<IssueIcon>(new()), StringComparison.Ordinal);
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
        Assert.Equal("15", table.GetAttribute("data-paged"));
        Assert.Equal(15, DataTable<int>.PagedRowCount);
        Assert.Equal("records", table.GetAttribute("data-rows-noun"));
        Assert.Equal(40, table.QuerySelectorAll("tbody tr").Length);
        Assert.Empty(table.QuerySelectorAll("tbody tr[hidden]"));
        Assert.Null(document.QuerySelector("nav.pager"));
    }

    [Fact]
    public async Task APagedTableWithoutACaptionIsRefusedBecauseItsPagerHasNoNameToUse()
    {
        var parameters = new Dictionary<string, object?>
        {
            [nameof(DataTable<int>.Items)] = (IReadOnlyList<int>)[1],
            [nameof(DataTable<int>.Head)] = (RenderFragment)(builder => builder.AddContent(0, "")),
            [nameof(DataTable<int>.Row)] = (RenderFragment<int>)(_ => builder => builder.AddContent(0, "")),
            [nameof(DataTable<int>.PagedRows)] = "rows"
        };

        await Assert.ThrowsAsync<ArgumentException>(() => RenderAsync<DataTable<int>>(parameters));
    }

    [Fact]
    public void TheReleaseShipsTheLicenseOfEveryFontItShips()
    {
        var assets = new ReleaseRenderer(null, null).Assets;

        Assert.All(assets, asset => Assert.Matches("^[a-z0-9._-]{1,80}(/[a-z0-9._-]{1,80})*$", asset.RelativePath));
        var licenseOf = new Dictionary<string, string>
        {
            ["assets/fonts/schibsted-grotesk.woff2"] = "assets/fonts/ofl-schibsted-grotesk.txt",
            ["assets/fonts/newsreader.woff2"] = "assets/fonts/ofl-newsreader.txt"
        };
        var fonts = assets.Where(asset => asset.RelativePath.EndsWith(".woff2", StringComparison.Ordinal)).Select(asset => asset.RelativePath);
        Assert.Equal(licenseOf.Keys.Order(StringComparer.Ordinal), fonts.Order(StringComparer.Ordinal));
        Assert.All(licenseOf.Values, path => Assert.Contains("SIL OPEN FONT LICENSE", System.Text.Encoding.UTF8.GetString(assets.Single(asset => asset.RelativePath == path).Content), StringComparison.Ordinal));
    }

    [Fact]
    public void ThePublicTokensReachFourAndAHalfToOneInBothThemesOnTheSurfacesThatShowThem()
    {
        var theme = File.ReadAllText(Path.Combine(RepositoryRoot.FullName, "src", "CivicLens.Host", "wwwroot", "css", "theme.css"));
        var supports = theme[theme.IndexOf("@supports (color: light-dark", StringComparison.Ordinal)..];
        var noSupport = supports[supports.IndexOf("@supports not", StringComparison.Ordinal)..supports.IndexOf("@media", StringComparison.Ordinal)];
        supports = supports[..supports.IndexOf("@supports not", StringComparison.Ordinal)];
        var fallback = Declarations(theme[theme.IndexOf("body.public {", StringComparison.Ordinal)..theme.IndexOf("@supports (color: light-dark", StringComparison.Ordinal)]);
        var publicTokens = fallback.Concat(Declarations(supports)).GroupBy(token => token.Key).ToDictionary(group => group.Key, group => group.Last().Value);
        var lightRoot = Declarations(theme[..theme.IndexOf("body.public {", StringComparison.Ordinal)]);
        var darkRoot = Declarations(theme[theme.IndexOf(":root[data-theme=\"dark\"] {", StringComparison.Ordinal)..]);

        foreach (var name in new[] { "--ins", "--ins-bg", "--del", "--del-bg", "--warn", "--warn-bg" })
            Assert.Equal(lightRoot[name], Declarations(noSupport)[name]);
        foreach (var (name, value) in publicTokens.Where(token => token.Value.StartsWith("light-dark", StringComparison.Ordinal)))
            Assert.Equal(Split(value)[0], fallback[name]);

        for (var dark = 0; dark < 2; dark++)
        {
            string Value(string name) => publicTokens.TryGetValue(name, out var token) && token.StartsWith("light-dark", StringComparison.Ordinal) ? Split(token)[dark]
                : publicTokens.TryGetValue(name, out token) ? token
                : dark == 1 && darkRoot.TryGetValue(name, out token) ? token : lightRoot[name];
            foreach (var surfaceName in new[] { "--ground", "--surface" })
            {
                var surface = Color.Parse(Value(surfaceName), null);
                void Reaches(string text, string background, Color under)
                {
                    var back = Color.Parse(Value(background), under);
                    Assert.True(Color.Parse(Value(text), back).ContrastWith(back) >= 4.5, $"{text} on {background} over {surfaceName}, theme {dark}");
                }

                foreach (var text in new[] { "--ink", "--ink-2", "--muted", "--info" })
                {
                    Reaches(text, "--ground", surface);
                    Reaches(text, "--surface", surface);
                    Reaches(text, "--raised", surface);
                }

                foreach (var text in new[] { "--ink", "--ink-2", "--muted", "--info" })
                    Reaches(text, "--info-bg", surface);
                Reaches("--ins", "--ins-bg", surface);
                Reaches("--del", "--del-bg", surface);
                Reaches("--warn", "--warn-bg", surface);
                Reaches("--primary-ink", "--primary", surface);
            }

            var bar = Color.Parse(Value("--bar"), null);
            foreach (var text in new[] { "--bar-ink", "--bar-muted" })
            {
                Assert.True(Color.Parse(Value(text), bar).ContrastWith(bar) >= 4.5, $"{text} on --bar, theme {dark}");
                var field = Color.Parse(Value("--bar-field"), bar);
                Assert.True(Color.Parse(Value(text), field).ContrastWith(field) >= 4.5, $"{text} on --bar-field, theme {dark}");
            }
        }
    }

    private static Dictionary<string, string> Declarations(string css) =>
        Regex.Matches(css, @"(--[a-z0-9-]+):\s*([^;]+);").GroupBy(match => match.Groups[1].Value).ToDictionary(group => group.Key, group => group.Last().Groups[2].Value);

    private static string[] Split(string lightDark)
    {
        var match = Regex.Match(lightDark, @"^light-dark\((.+?),\s*(?=(?:#|rgba))(.+)\)$");
        return [match.Groups[1].Value, match.Groups[2].Value];
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
