using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using CivicLens.PublicWeb.Publication;
using CivicLens.Publication.Contracts;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace CivicLens.Tests.PublicWeb;

public sealed class ZipMemberCardsTests
{
    private const string Root = "https://civic-lens.test/";
    private const string Scope = "Published roll calls listing this member. Missed means Not Voting.";
    private static readonly ZipMemberAttendanceData House = new(PublishedChamber.House, 120, 2, 12, new(2027, 1, 4), new(2027, 2, 5), Scope);

    [Fact]
    public async Task ACardNamesTheMemberSeatPartyTermAndScopedAttendanceWhileMissingEvidenceStaysAbsent()
    {
        var photo = PublicationProtocol.PhotoPath(new string('a', 64));
        var member = new ZipMemberIdentityData("pat", "Pat Member", "Democratic", new(2029, 1, 3), photo, [House]);
        var data = new ZipMemberCardsData(8, 119, "36009", 2,
            [new("Representative for Alabama, district 2", member), new("Representative for Alabama, district 1", member with { OfficialId = "other", Name = "Other Member", Party = null, TermEndsOn = null, PhotoPath = null, Attendance = [] }), new("Senator for Alabama", null)], []);

        var document = await RenderAsync(data);
        var cards = document.QuerySelectorAll(".zip-member-card");
        Assert.Equal(3, cards.Length);
        var known = cards[0];
        Assert.Equal("Pat Member", known.QuerySelector("h3 a")!.TextContent);
        Assert.Equal(Root + PublicationProtocol.OfficialPagePath("pat"), known.QuerySelector("h3 a")!.GetAttribute("href"));
        Assert.Contains("Representative for Alabama, district 2", known.TextContent);
        Assert.Contains("Democratic", known.TextContent);
        Assert.Equal("2029-01-03", known.QuerySelector("time")!.GetAttribute("datetime"));
        Assert.Equal("3 Jan 2029", known.QuerySelector("time")!.TextContent);
        Assert.Equal(Root + photo, known.QuerySelector("img")!.GetAttribute("src"));
        Assert.Null(known.QuerySelector("figcaption"));
        var figure = known.QuerySelector(".zip-member-attendance")!;
        Assert.Contains("2 of 12", figure.TextContent);
        Assert.Contains("House roll calls listing this member in Congress 120", figure.TextContent);
        Assert.Contains(Scope, figure.TextContent);
        Assert.Equal(["2027-01-04", "2027-02-05"], figure.QuerySelectorAll("time").Select(day => day.GetAttribute("datetime")));
        Assert.Null(cards[1].QuerySelector(".zip-member-attendance"));
        Assert.NotNull(cards[1].QuerySelector("svg.portrait-placeholder"));
        Assert.Contains("No published roll calls list this member.", cards[1].TextContent);
        Assert.Contains("Our records show no end date for this term.", cards[1].TextContent);
        Assert.Null(cards[2].QuerySelector("a"));
        Assert.Contains("No current member is in our records.", cards[2].TextContent);
        Assert.Contains("lies in 2 districts", document.QuerySelector(".zip-count")!.TextContent);
    }

    [Fact]
    public async Task BothChambersKeepTheirMetricCongressAndEachScopeButtonTargetsItsOwnEvidence()
    {
        var senate = House with { Chamber = PublishedChamber.Senate, Missed = 0, Held = 1 };
        var member = new ZipMemberIdentityData("pat", "Pat Member", null, null, null, [House, senate]);
        var document = await RenderAsync(new(9, 119, "36009", 1, [new("Senator for Alabama", member), new("Representative for Alabama, district 2", member)], []));

        var fragment = document.QuerySelector("[data-zip-member-fragment]")!;
        Assert.Equal("119", fragment.GetAttribute("data-congress"));
        Assert.Equal("9", fragment.GetAttribute("data-release"));
        Assert.Equal("36009", fragment.GetAttribute("data-zcta"));
        var figures = document.QuerySelectorAll(".zip-member-attendance");
        Assert.Equal(4, figures.Length);
        Assert.All(figures, figure => Assert.Contains("Congress 120", figure.TextContent));
        Assert.Contains("0 of 1", figures[1].TextContent);
        Assert.Contains("Senate roll call listing", figures[1].TextContent);
        var targets = document.QuerySelectorAll("button[popovertarget]").Select(button => button.GetAttribute("popovertarget")!).ToArray();
        Assert.Equal(4, targets.Distinct(StringComparer.Ordinal).Count());
        Assert.All(targets, id => Assert.NotNull(document.GetElementById(id)));
        Assert.Null(document.QuerySelector(".zip-count"));
    }

    [Fact]
    public async Task StoredTextIsEscapedAndATerritoryGetsItsOwnNoSenatorsMessage()
    {
        const string text = "<script>stored()</script>";
        var member = new ZipMemberIdentityData("member", text, text, null, null, [House with { Scope = text }]);
        var document = await RenderAsync(new(10, 119, "20001", 1, [new(text, member)], ["District of Columbia"]));

        Assert.Empty(document.QuerySelectorAll("script"));
        Assert.Equal(text, document.QuerySelector("h3 a")!.TextContent);
        Assert.Contains(text, document.QuerySelector(".term-popover")!.TextContent);
        Assert.Contains("District of Columbia has no senators.", document.DocumentElement!.TextContent);
    }

    private static async Task<IDocument> RenderAsync(ZipMemberCardsData data)
    {
        await using var services = new ServiceCollection().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, NullLoggerFactory.Instance);
        var html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<ZipMemberCards>(ParameterView.FromDictionary(
                new Dictionary<string, object?> { [nameof(ZipMemberCards.Data)] = data, [nameof(ZipMemberCards.RootPath)] = Root }));
            return output.ToHtmlString();
        });
        return new HtmlParser().ParseDocument(html);
    }
}
