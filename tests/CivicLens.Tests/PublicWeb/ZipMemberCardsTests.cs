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
    private static readonly ZipMemberPartyLineData HouseDemocrat = new(PublishedChamber.House, 119, "Democrat", 248, 249,
        new(2025, 1, 3), new(2026, 12, 1), "Yea or Nay casts by the official as Democrat on substantive split roll calls of the House in Congress 119. Party is pinned to each vote.");

    [Theory]
    [InlineData("health", "Health <script>alert(1)</script>", false)]
    [InlineData("unknown<issue>", "Health", true)]
    public async Task IssueControlsRetainTheSelectedFilterAndEscapeNamesWhileAnEmptyResultStaysEmpty(string selected, string name, bool unknown)
    {
        var data = new ZipMemberCardsData(8, 119, "36009", 1, [new("Representative for Alabama", null)], [])
        {
            IssueOptions = [new("health", name), new("housing", "Housing")],
            SelectedIssue = selected
        };

        var document = await RenderAsync(data);
        var controls = document.QuerySelector("[data-member-issue-filter]")!;
        Assert.True(controls.HasAttribute("hidden"));
        Assert.Empty(document.QuerySelectorAll("script"));
        Assert.Equal(selected, document.QuerySelector("[data-zip-member-fragment]")!.GetAttribute("data-issue"));
        Assert.Equal(selected, Assert.Single(controls.QuerySelectorAll("[aria-pressed=true]")).GetAttribute("data-member-issue"));
        Assert.Equal(unknown ? 4 : 3, controls.QuerySelectorAll("button").Length);
        Assert.Equal(name, controls.QuerySelector("[data-member-issue=health]")!.TextContent);
        Assert.Equal("false", controls.QuerySelector("[data-member-issue='']")!.GetAttribute("aria-pressed"));
        Assert.Empty(document.QuerySelectorAll(".shared-vote-row"));
        Assert.Equal("No records match.", document.QuerySelector("[data-member-votes-empty]")!.TextContent);
    }

    [Fact]
    public async Task ACardNamesTheMemberSeatPartyTermAndScopedAttendanceWhileMissingEvidenceStaysAbsent()
    {
        var photo = PublicationProtocol.PhotoPath(new string('a', 64));
        var member = new ZipMemberIdentityData("pat", "Pat Member", "Democratic", new(2029, 1, 3), photo, [House], [HouseDemocrat]);
        var data = new ZipMemberCardsData(8, 119, "36009", 2,
            [new("Representative for Alabama, district 2", member), new("Representative for Alabama, district 1", member with { OfficialId = "other", Name = "Other Member", Party = null, TermEndsOn = null, PhotoPath = null, Attendance = [], PartyLines = [] }), new("Senator for Alabama", null)], []);

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
        var partyLine = known.QuerySelector(".zip-member-party-line")!;
        Assert.Contains("With most House Democrats on 248 of 249 split votes in Congress 119.", partyLine.TextContent);
        Assert.Contains(HouseDemocrat.Scope, partyLine.TextContent);
        Assert.Equal(["2025-01-03", "2026-12-01"], partyLine.QuerySelectorAll("time").Select(day => day.GetAttribute("datetime")));
        Assert.Null(cards[1].QuerySelector(".zip-member-attendance"));
        Assert.Null(cards[1].QuerySelector(".zip-member-party-line"));
        Assert.NotNull(cards[1].QuerySelector("svg.portrait-placeholder"));
        Assert.Contains("No published roll calls list this member.", cards[1].TextContent);
        Assert.Contains("Our records show no end date for this term.", cards[1].TextContent);
        Assert.Null(cards[2].QuerySelector("a"));
        Assert.Contains("No current member is in our records.", cards[2].TextContent);
        Assert.Contains("lies in 2 districts", document.QuerySelector(".zip-count")!.TextContent);
    }

    [Fact]
    public async Task MemberFiguresUseTheirOwnCongressAndIssueNamesAndScopeDetails()
    {
        var member = new ZipMemberIdentityData("pat", "Pat Member", null, null, null, [], [])
        {
            Bills = new(119, 1, "Sponsored bills in Congress 119."),
            Issues = [new("Firearms", 118, 2, 3, "Counts for firearms in Congress 118.", "issues/firearms.html"), new("Issue 7", 118, 0, 0, "Counts for issue 7 in Congress 118.")]
        };
        var document = await RenderAsync(new(9, 120, "36009", 1, [new("Representative for Alabama, district 2", member)], []));

        Assert.Contains("Bills sponsored in Congress 119", document.QuerySelector(".zip-member-bills")!.TextContent);
        Assert.Contains("1 bill", document.QuerySelector(".zip-member-bills")!.TextContent);
        Assert.Contains("Most active issues in Congress 118", document.QuerySelector(".zip-member-issues")!.TextContent);
        Assert.Contains("Firearms: 2 bills sponsored, 3 substantive votes cast.", document.QuerySelector(".zip-member-issues")!.TextContent);
        Assert.Contains("Issue 7: 0 bills sponsored, 0 substantive votes cast.", document.QuerySelector(".zip-member-issues")!.TextContent);
        Assert.Equal(Root + "issues/firearms.html", document.QuerySelector(".zip-member-issues a")!.GetAttribute("href"));
        Assert.Equal("Firearms", document.QuerySelector(".zip-member-issues a")!.TextContent);
        Assert.DoesNotContain(document.QuerySelectorAll(".zip-member-issues a"), link => link.TextContent == "Issue 7");
        Assert.Equal(3, document.QuerySelectorAll("button[popovertarget]").Length);
        Assert.Contains("Sponsored bills in Congress 119.", document.QuerySelectorAll(".term-popover")[0].TextContent);
        Assert.Contains("Counts for firearms in Congress 118.", document.QuerySelectorAll(".term-popover")[1].TextContent);
    }

    [Fact]
    public async Task SharedRowsRenderInsideTheSingleZipMemberFragment()
    {
        var vote = new SharedVoteRowData(new string('c', 64), new(2026, 9, 16), 119, 2, 314,
            "House passes a bill", null, "Vote to pass", "Passed", [new("pat", "Yes (Yea)", "Yea")]);
        var shared = new SharedVoteRowsData(PublishedChamber.House, [new("pat", "Pat Member")], [vote]);
        var member = new ZipMemberIdentityData("pat", "Pat Member", null, null, null, [], []);
        var document = await RenderAsync(new ZipMemberCardsData(9, 119, "36009", 1, [new("Representative for Alabama, district 2", member)], [])
        {
            SharedVotes = [shared]
        });

        Assert.Single(document.QuerySelectorAll("[data-zip-member-fragment]"));
        Assert.Equal(9, int.Parse(document.QuerySelector("[data-zip-member-fragment]")!.GetAttribute("data-release")!));
        Assert.Single(document.QuerySelectorAll(".shared-vote-rows"));
        Assert.Equal("Yes (Yea)", document.QuerySelector(".shared-vote-cast-value")!.TextContent);
    }

    [Fact]
    public async Task BothChambersKeepTheirMetricCongressAndEachScopeButtonTargetsItsOwnEvidence()
    {
        var senate = House with { Chamber = PublishedChamber.Senate, Missed = 0, Held = 1 };
        var member = new ZipMemberIdentityData("pat", "Pat Member", null, null, null, [House, senate], []);
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
    public async Task PartyLinesKeepEachPinnedPartyAndCongressAndUseUniqueEvidencePopovers()
    {
        var senateRepublican = HouseDemocrat with { Chamber = PublishedChamber.Senate, Party = "Republican", Congress = 118, WithParty = 93, Cast = 100, FirstVotedOn = new(2023, 1, 3), LastVotedOn = new(2024, 12, 1), Scope = "Senate Republican split votes in Congress 118." };
        var houseRepublican = HouseDemocrat with { Party = "Republican", Congress = 120, WithParty = 17, Cast = 20, FirstVotedOn = new(2027, 1, 3), LastVotedOn = new(2027, 12, 1), Scope = "House Republican split votes in Congress 120." };
        var member = new ZipMemberIdentityData("pat", "Pat Member", "Democratic", null, null, [House], [HouseDemocrat, senateRepublican, houseRepublican]);
        var document = await RenderAsync(new(9, 122, "36009", 1, [new("Representative for Alabama, district 2", member)], []));

        var figures = document.QuerySelectorAll(".zip-member-party-line");
        Assert.Equal(3, figures.Length);
        Assert.Contains("With most House Democrats on 248 of 249 split votes in Congress 119.", figures[0].TextContent);
        Assert.Contains("With most Senate Republicans on 93 of 100 split votes in Congress 118.", figures[1].TextContent);
        Assert.Contains("With most House Republicans on 17 of 20 split votes in Congress 120.", figures[2].TextContent);
        Assert.Contains(senateRepublican.Scope, figures[1].QuerySelector(".term-popover")!.TextContent);
        Assert.Equal(["2023-01-03", "2024-12-01"], figures[1].QuerySelectorAll("time").Select(day => day.GetAttribute("datetime")));
        Assert.Contains(houseRepublican.Scope, figures[2].QuerySelector(".term-popover")!.TextContent);
        var targets = document.QuerySelectorAll("button[popovertarget]").Select(button => button.GetAttribute("popovertarget")!).ToArray();
        Assert.Equal(4, targets.Length);
        Assert.Equal(4, targets.Distinct(StringComparer.Ordinal).Count());
        Assert.All(targets, id => Assert.NotNull(document.GetElementById(id)));
    }

    [Fact]
    public async Task PartyLinesWithoutPositiveCastsAreOmitted()
    {
        var member = new ZipMemberIdentityData("pat", "Pat Member", "Democratic", null, null, [],
            [HouseDemocrat with { WithParty = 0, Cast = 0 }]);
        var document = await RenderAsync(new(9, 119, "36009", 1, [new("Representative for Alabama, district 2", member)], []));

        Assert.Empty(document.QuerySelectorAll(".zip-member-party-line"));
    }

    [Fact]
    public async Task StoredTextIsEscapedAndATerritoryGetsItsOwnNoSenatorsMessage()
    {
        const string text = "<script>stored()</script>";
        var member = new ZipMemberIdentityData("member", text, text, null, null, [House with { Scope = text }], [HouseDemocrat with { Party = text, Scope = text }]);
        var document = await RenderAsync(new(10, 119, "20001", 1, [new(text, member)], ["District of Columbia"]));

        Assert.Empty(document.QuerySelectorAll("script"));
        Assert.Equal(text, document.QuerySelector("h3 a")!.TextContent);
        Assert.Contains(text, document.QuerySelector(".term-popover")!.TextContent);
        Assert.Contains(text, document.QuerySelectorAll(".term-popover")[1].TextContent);
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
