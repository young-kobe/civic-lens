using System.Collections.Immutable;
using AngleSharp.Html.Parser;
using CivicLens.Application.Issues;
using CivicLens.Application.Publication;
using CivicLens.Host.Publication;
using CivicLens.Publication.Contracts;

namespace CivicLens.Tests.Host;

public sealed class PublishedHomePageTests
{
    private static readonly DateTimeOffset Published = new(2026, 5, 11, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task TheBreakCardListsOneRowForEachLeaderInTheOrderOfTheHighlightsAndALinkToTheBoards()
    {
        var high = Leader("High Share", breaks: 126, cast: 206, pagePath: "officials/high.html");
        var low = Leader("Low Share", breaks: 10, cast: 100);

        var main = await HomeAsync(ReleaseOf(), [high, low]);

        Assert.Equal("Who broke from their party most", main.QuerySelector(".break-card h2")!.TextContent);
        var rows = main.QuerySelectorAll(".break-rows li").ToList();
        Assert.Equal(["High Share", "Low Share"], rows.Select(row => row.QuerySelector("a")!.TextContent));
        Assert.Equal("officials/high.html", rows[0].QuerySelector("a")!.GetAttribute("href"));
        Assert.Equal("126 of 206 votes", rows[0].QuerySelector(".break-row-text > .muted")!.TextContent);
        Assert.Equal(("126", "206"), (rows[0].QuerySelector("meter")!.GetAttribute("value"), rows[0].QuerySelector("meter")!.GetAttribute("max")));
        Assert.Equal(("10", "100"), (rows[1].QuerySelector("meter")!.GetAttribute("value"), rows[1].QuerySelector("meter")!.GetAttribute("max")));
        Assert.Equal("officials.html", main.QuerySelector(".break-card > p > a")!.GetAttribute("href"));
        Assert.Equal("See all four party boards", main.QuerySelector(".break-card > p > a")!.TextContent);
    }

    [Fact]
    public async Task TheBreakCardNamesTheCongressOfItsRowsSoAKeptEarlierBoardIsNotPassedOffAsTheCurrentOne()
    {
        var current = Leader();
        var kept = Leader(chamber: PublishedChamber.Senate) with { Congress = 118 };

        var same = await HomeAsync(ReleaseOf(), [current]);
        var mixed = await HomeAsync(ReleaseOf(), [current, kept]);

        Assert.Equal("The share of split votes each member cast against most of their party, in the 119th Congress.", same.QuerySelector(".break-card-lede")!.TextContent);
        Assert.Equal("The share of split votes each member cast against most of their party, in the 119th and 118th Congresses.", mixed.QuerySelector(".break-card-lede")!.TextContent);
    }

    [Fact]
    public async Task TheInfoButtonOfTheBreakCardOpensTheFixedRuleAndSaysWhenAChamberAndPartyHasNoLine()
    {
        var one = await HomeAsync(ReleaseOf(), [Leader()]);

        var button = one.QuerySelector(".break-card-head button")!;
        Assert.Equal("How the card chooses these members", button.GetAttribute("aria-label"));
        var rule = one.QuerySelector("#" + button.GetAttribute("popovertarget"))!;
        Assert.Contains("at least 25 split votes", rule.TextContent, StringComparison.Ordinal);
        Assert.Contains("A vote against the party is not a mistake.", rule.TextContent, StringComparison.Ordinal);
        Assert.Contains("A chamber and party with no line has no member with vote figures on this site who meets these rules.", rule.TextContent, StringComparison.Ordinal);
        Assert.Equal("How these choices are made", rule.QuerySelector("a[href='method.html#home-picks']")!.TextContent);

        var all = await HomeAsync(ReleaseOf(), [.. OfficialBoard.All.Select(board => Leader(chamber: board.Chamber) with { Party = board.Party })]);
        Assert.DoesNotContain("A chamber and party with no line", all.QuerySelector(".break-card-head .term-popover")!.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheOldPanelsButtonsTablesAndReleaseLineAreGoneFromHomeAndTheHeroHoldsTheHeadingAndTheLede()
    {
        var release = ReleaseOf(Vote('a', 216, 214, PublishedVoteAlignment.Split), Executive('b', 1));

        var main = await HomeAsync(release, [Leader()]);

        var document = main;
        Assert.Equal("Who represents you?", document.QuerySelector(".public-hero h1")!.TextContent);
        Assert.Equal("See how your representative and senators voted and which bills they introduced. Each fact links to its official source.", document.QuerySelector(".public-hero .home-lead")!.TextContent);
        foreach (var gone in new[] { "Chosen by a fixed rule", "Closest vote", "Newest executive actions", "Newest records", "Find your representative or senators", "Understand a bill", "Search all records", "Release 3", "Policy areas", "Voted against their party most often" })
            Assert.DoesNotContain(gone, document.TextContent, StringComparison.Ordinal);
        Assert.Empty(document.QuerySelectorAll("main table, main form, .home-actions"));
    }

    [Fact]
    public async Task HomeLinksTheOfficialsListWithTheRealCountInPlaceOfTheZipForm()
    {
        var main = await HomeAsync(ReleaseOf(), [], officialCount: 541);

        var link = main.QuerySelector(".home-browse a")!;
        Assert.Equal("browse all 541 officials", link.TextContent);
        Assert.Equal("officials.html", link.GetAttribute("href"));
    }

    [Fact]
    public async Task TheLatestListHoldsTheNewestVotesOfEachChamberAndTheThreeNewestActionsNewestFirstWithTheirDateAndALink()
    {
        var split = PublishedVoteAlignment.Split;
        var release = ReleaseOf(Vote('a', 1, 1, split, 1), Vote('b', 1, 1, split, 2), Vote('c', 1, 1, split, 3) with { Chamber = PublishedChamber.Senate }, Vote('d', 1, 1, split, 4), Vote('e', 1, 1, split, 5),
            Executive('f', 1), Executive('g', 2), Executive('h', 3), Executive('i', 4));

        var main = await HomeAsync(release, []);

        var section = main.QuerySelector("section#latest")!;
        Assert.Equal("Latest from the record", section.QuerySelector("h2")!.TextContent);
        var columns = section.QuerySelectorAll(".latest-columns > div").ToList();
        Assert.Equal(["Votes", "Executive actions"], columns.Select(column => column.QuerySelector("h3")!.TextContent));
        Assert.Equal([Id('e'), Id('d'), Id('c')], columns[0].QuerySelectorAll("li a").Select(link => link.GetAttribute("href")!.Split('/')[1][..32]));
        Assert.Equal([Id('i'), Id('h'), Id('g')], columns[1].QuerySelectorAll("li a").Select(link => link.GetAttribute("href")!.Split('/')[1][..32]));
        Assert.Equal(["Headline e", "House, 5 May 2026"], columns[0].QuerySelector("li a")!.Children.Select(child => child.TextContent));
        Assert.Equal(["Headline c", "Senate, 3 May 2026"], columns[0].QuerySelectorAll("li a")[2].Children.Select(child => child.TextContent));
        Assert.Equal(["Headline i", "4 Apr 2026"], columns[1].QuerySelector("li a")!.Children.Select(child => child.TextContent));
    }

    [Fact]
    public async Task TheLatestListAndTheBreakCardAreHiddenWhenTheyHaveNothingToShowInsteadOfAnEmptyBlock()
    {
        var main = await HomeAsync(ReleaseOf(), []);

        Assert.Null(main.QuerySelector("#latest"));
        Assert.Null(main.QuerySelector("#issues"));
        Assert.Null(main.QuerySelector(".break-card"));
    }

    [Fact]
    public async Task AColumnWithNoEntryIsHiddenSoTheOtherColumnStandsAlone()
    {
        var main = await HomeAsync(ReleaseOf(Vote('a', 1, 1, PublishedVoteAlignment.Split, 1)), []);

        var columns = main.QuerySelectorAll("section#latest .latest-columns > div").ToList();
        Assert.Equal(["Votes"], columns.Select(column => column.QuerySelector("h3")!.TextContent));
    }

    [Fact]
    public async Task TheIssueTilesLinkEachIssuePageAndDrawTheConfiguredIconOrTheNeutralDefaultAndSkipAnIssueWithNoPage()
    {
        ReleaseFacet Facet(string id, string? icon) => new(FacetKind.Issue, id, $"Name {id}", [], icon, null);
        var facets = new ReleaseFacets([Facet("firearms", "crosshair"), Facet("plain", null), Facet("not a segment", "bolt")], []);

        var main = await HomeAsync(ReleaseOf(), [], facets: facets);

        var section = main.QuerySelector("section#issues")!;
        Assert.Equal("Start with an issue", section.QuerySelector("h2")!.TextContent);
        var tiles = section.QuerySelectorAll("a.issue-tile").ToList();
        Assert.Equal(["Name firearms", "Name plain"], tiles.Select(tile => tile.TextContent));
        Assert.Equal(PublicationProtocol.IssuePagePath("firearms", 1), tiles[0].GetAttribute("href"));
        Assert.Equal(IssueIcons.PathOf("crosshair"), tiles[0].QuerySelector("path")!.GetAttribute("d"));
        Assert.Equal(IssueIcons.PathOf(IssueIcons.Default), tiles[1].QuerySelector("path")!.GetAttribute("d"));
    }

    [Fact]
    public async Task TheHomeTileOfAnIssueDrawsTheIconTheCatalogHoldsSoABrokenLookupShowsTheNeutralIcon()
    {
        var issues = new IssueConfiguration
        {
            PolicyAreas = [new() { Id = "defense", Name = "Defense" }],
            Issues = [new() { Id = "firearms", Name = "Firearms", Definition = "d", PolicyAreaIds = ["defense"] }]
        };
        var icons = new IssueIconConfiguration { Icons = new Dictionary<string, string> { ["firearms"] = "crosshair" } };
        var release = ReleaseOf(Vote('a', 216, 214, null) with { IssueIds = ["firearms"] });
        var facets = ReleaseFacets.From(release, new PublicationCatalog(new Dictionary<string, string>(), issues, icons));

        var main = await HomeAsync(release, [], facets: facets);

        var tile = main.QuerySelector("section#issues a.issue-tile")!;
        Assert.Equal(IssueIcons.PathOf("crosshair"), tile.QuerySelector("path")!.GetAttribute("d"));
    }

    [Theory]
    [InlineData("Independent", "Democrat", "ME", "I, Maine")]
    [InlineData("Democrat", null, null, "D")]
    [InlineData(null, "Democrat", "TX", "Texas")]
    [InlineData("Libertarian", "Democrat", "ME", "Maine")]
    [InlineData("Democrat", null, "DC", "D, District of Columbia")]
    public async Task TheBreakCardShowsOnlyThePartyLetterAndStateNameThatTheTermHolds(string? party, string? caucus, string? state, string expected)
    {
        var main = await HomeAsync(ReleaseOf(), [Leader(terms: [Term(party, state, caucus)])]);

        Assert.Equal(expected, main.QuerySelector(".break-row-name .muted")!.TextContent);
    }

    [Fact]
    public async Task TheBreakCardUsesThePartyOfTheCountedChamberSoAMemberWhoChangedPartyIsNotLabeledWithTheNewParty()
    {
        var main = await HomeAsync(ReleaseOf(), [Leader(terms: [Term("Democrat", "TX", startYear: 2021), Term("Republican", "FL", startYear: 2025)])]);

        Assert.Equal("D, Texas", main.QuerySelector(".break-row-name .muted")!.TextContent);
    }

    [Fact]
    public async Task ALaterTermInAnotherOfficeOrChamberDoesNotChangeThePartyOrStateOfAMemberOfTheHouse()
    {
        var other = Leader(terms: [Term("Democrat", "TX", startYear: 2021), Term("Democrat", null, startYear: 2025, kind: PublishedOfficeKind.Executive), Term("Democrat", "NM", startYear: 2026, kind: PublishedOfficeKind.Senator)]);

        var main = await HomeAsync(ReleaseOf(), [other]);

        Assert.Equal("D, Texas", main.QuerySelector(".break-row-name .muted")!.TextContent);
    }

    [Fact]
    public async Task ASenateLeaderTakesTheStateOfTheSenateTermNotOfALaterHouseTerm()
    {
        var terms = new[] { Term("Democrat", "NM", startYear: 2021, kind: PublishedOfficeKind.Senator), Term("Democrat", "TX", startYear: 2025) };

        var main = await HomeAsync(ReleaseOf(), [Leader(terms: terms, chamber: PublishedChamber.Senate)]);

        Assert.Equal("D, New Mexico", main.QuerySelector(".break-row-name .muted")!.TextContent);
    }

    [Fact]
    public async Task TheBreakCardLeavesOutThePartyAndStateItDoesNotHold()
    {
        var main = await HomeAsync(ReleaseOf(), [Leader(terms: [])]);

        Assert.Equal("", main.QuerySelector(".break-row-name .muted")!.TextContent);
    }

    [Theory]
    [InlineData(PublishedVoteAlignment.Split, "Split vote", "A split vote is a substantive roll call vote where most Democrats and most Republicans who cast Yes or No voted on opposite sides.")]
    [InlineData(PublishedVoteAlignment.PartiesAgree, "Both parties agreed", "A vote where both parties agreed is a substantive roll call vote where most Democrats and most Republicans who cast Yes or No voted the same way.")]
    public async Task AVoteInAListShowsItsPartyPillAndCountsAndTheListSaysTheirMeaningOnceAboveTheRows(PublishedVoteAlignment alignment, string badge, string meaning)
    {
        var main = await AllRecordsAsync(ReleaseOf(Vote('a', 216, 214, alignment)));

        var row = main.QuerySelector("tbody tr")!;
        Assert.Equal([badge], row.QuerySelectorAll(".badge").Select(item => item.TextContent));
        Assert.Equal("Vote, Yes 216, No 214", row.QuerySelector(".cell-note")!.TextContent);
        Assert.DoesNotContain(meaning, row.TextContent, StringComparison.Ordinal);
        Assert.Equal(1, main.TextContent.Split(meaning).Length - 1);
        Assert.Contains("Yes and No counts are casts of all members of the chamber who voted Yes or No.", main.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ACandidateVoteAndAMixedVoteGetNoPartyPillAndACandidateVoteShowsNoCounts()
    {
        var candidate = Vote('a', 0, 0, null) with { YeaCount = null, NayCount = null };
        var mixed = Vote('b', 50, 50, PublishedVoteAlignment.Mixed);

        var main = await AllRecordsAsync(ReleaseOf(candidate, mixed));

        var rows = main.QuerySelectorAll("tbody tr").ToList();
        Assert.All(rows, row => Assert.Empty(row.QuerySelectorAll(".badge")));
        Assert.Equal("Vote", rows.Single(row => row.QuerySelector($"a[href='records/{Id('a')}.html']") is not null).QuerySelector(".cell-note")!.TextContent);
        Assert.DoesNotContain("A split vote is", main.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheLatestVotesShowTheBillLineOfTheirActionWithTheSourceNoteOnceAndShowNoNoteWhenNoVoteHoldsALine()
    {
        var action = Entry(PublishedRecordKind.Action, 'b') with { BillLine = "This bill funds rent aid." };
        var votes = new[] { Vote('a', 216, 214, PublishedVoteAlignment.Split), Vote('c', 300, 100, PublishedVoteAlignment.PartiesAgree) };
        var release = ReleaseOf([action, .. votes]);
        var lines = ReleaseBillLines.From(release.Records, new Dictionary<string, ImmutableArray<string>> { [Id('a')] = [action.RecordId] });
        var withLine = HomeHighlights.From(release, [], 0) with { BillLines = lines };

        var shown = new HtmlParser().ParseDocument(await new ReleaseRenderer(null, null).RenderHomeAsync(release, null, new ReleaseFacets([], []), withLine, CancellationToken.None)).Body!;
        var bare = await HomeAsync(release, []);

        Assert.Equal(["This bill funds rent aid."], shown.QuerySelectorAll("#latest .bill-line").Select(line => line.TextContent));
        Assert.Equal(1, shown.TextContent.Split("From the Congressional Research Service summary.").Length - 1);
        Assert.DoesNotContain("From the Congressional Research Service summary.", bare.TextContent, StringComparison.Ordinal);
    }

    private static async Task<AngleSharp.Dom.IElement> AllRecordsAsync(PublicationRelease release) =>
        new HtmlParser().ParseDocument(await new ReleaseRenderer(null, null).RenderAllRecordsAsync(release, 1, ReleaseBillLines.None, CancellationToken.None)).QuerySelector("main")!;

    [Fact]
    public async Task TheZipFormCallsTheServiceAtTheConfiguredAddressAndStaysHiddenUntilScriptRuns()
    {
        var html = await HomeHtmlAsync("https://api.example.test");
        var document = new HtmlParser().ParseDocument(html);

        var form = document.QuerySelector("form[data-zip-api]")!;
        Assert.Equal("https://api.example.test", form.GetAttribute("data-zip-api"));
        Assert.True(form.HasAttribute("hidden"));
        Assert.Equal("officials/{id}.html", form.GetAttribute("data-official-page"));
        Assert.Equal("officials.html", form.GetAttribute("data-officials-page"));
        Assert.Equal("District of Columbia", System.Text.Json.JsonDocument.Parse(form.GetAttribute("data-states")!).RootElement.GetProperty("DC").GetString());
        var offices = System.Text.Json.JsonDocument.Parse(form.GetAttribute("data-house-offices")!).RootElement;
        Assert.Equal("Resident Commissioner", offices.GetProperty("PR").GetString());
        Assert.Equal("Delegate", offices.GetProperty("DC").GetString());
        Assert.Equal(6, offices.EnumerateObject().Count());
        var input = form.QuerySelector("input")!;
        Assert.Equal(("zip", "5", "numeric"), (input.GetAttribute("name"), input.GetAttribute("maxlength"), input.GetAttribute("inputmode")));
        Assert.Equal("Your ZIP code", form.QuerySelector("label[for='zip-code']")!.TextContent);
        Assert.Equal("5 digits", input.GetAttribute("placeholder"));
        Assert.Equal("Find my officials", form.QuerySelector("button[type=submit]")!.TextContent);
    }

    [Fact]
    public async Task TheZipFormHasAJavaScriptNoticeBeforeItBecauseTheFormIsHiddenWithoutScript()
    {
        var html = await HomeHtmlAsync("https://api.example.test");

        var noscriptStart = html.IndexOf("<noscript>", StringComparison.Ordinal);
        var noscriptEnd = html.IndexOf("</noscript>", StringComparison.Ordinal);
        var notice = html.IndexOf("The ZIP lookup needs JavaScript. Without it, browse ", StringComparison.Ordinal);
        Assert.True(noscriptStart >= 0);
        Assert.InRange(notice, noscriptStart, noscriptEnd);
        Assert.True(noscriptEnd < html.IndexOf("<form class=\"zip-form\"", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TheResultPanelIsHiddenUntilALookupAndCreditsTheCensusBureauAndSaysAZctaIsNotAZipCode()
    {
        var document = new HtmlParser().ParseDocument(await HomeHtmlAsync("https://api.example.test"));

        var panel = document.QuerySelector("[data-zip-results]")!;
        Assert.True(panel.HasAttribute("hidden"));
        Assert.Equal("Your officials", panel.QuerySelector("h2")!.TextContent);
        var heading = panel.QuerySelector("h2")!;
        var chip = document.QuerySelector("[data-your-members]")!;
        var membersLink = new Uri(new Uri("https://civic-lens.test/"), chip.QuerySelector("a")!.GetAttribute("href")!);
        Assert.NotEmpty(heading.Id!);
        Assert.Equal("#" + heading.Id, membersLink.Fragment);
        Assert.Single(document.QuerySelectorAll("#" + heading.Id));
        Assert.True(chip.HasAttribute("hidden"));
        Assert.Empty(panel.QuerySelector("[data-zip-body]")!.Children);
        Assert.Null(document.QuerySelector("input[name=zip]")!.GetAttribute("value"));
        Assert.Equal("status", panel.QuerySelector("[data-zip-body]")!.GetAttribute("role"));
        var notes = panel.QuerySelector("[data-zip-notes]")!;
        Assert.True(notes.HasAttribute("hidden"));
        Assert.Equal(["Source: U.S. Census Bureau", "A ZIP Code Tabulation Area is a Census Bureau area. It is not a USPS ZIP code. Near a border, check your address with your House office."],
            notes.QuerySelectorAll("p").Select(note => note.TextContent));
        Assert.Single(panel.QuerySelectorAll("[data-zip-notes]"));
    }

    [Fact]
    public void TheZipResultShowsTheTermEndDateInTheSiteFormatOrSaysTheRecordsHoldNoEndDate()
    {
        var script = System.Text.Encoding.UTF8.GetString(new ReleaseRenderer(null, null).Assets.Single(asset => asset.RelativePath == "assets/js/public.js").Content);

        Assert.Contains("holder.termEndsOn ? `Current term ends ${formatDay(holder.termEndsOn)}.` : \"Our records show no end date for this term.\"", script, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheZipFormHoldsAnAlertRegionNextToTheFieldSoAnErrorIsAnnounced()
    {
        var document = new HtmlParser().ParseDocument(await HomeHtmlAsync("https://api.example.test"));

        var alert = document.QuerySelector("form[data-zip-api] [data-zip-error]")!;
        Assert.Equal("alert", alert.GetAttribute("role"));
        Assert.Equal("", alert.TextContent);
    }

    [Fact]
    public void TheZipLookupShowsABusyStateThenBringsTheAnswerIntoViewAndMovesFocusToItsHeading()
    {
        var script = System.Text.Encoding.UTF8.GetString(new ReleaseRenderer(null, null).Assets.Single(asset => asset.RelativePath == "assets/js/public.js").Content);

        Assert.Contains("submit.disabled = true;", script, StringComparison.Ordinal);
        Assert.Contains("body.setAttribute(\"aria-busy\", \"true\");", script, StringComparison.Ordinal);
        Assert.Contains("panel.scrollIntoView({ block: \"start\", behavior: reducedMotion.matches ? \"auto\" : \"smooth\" });", script, StringComparison.Ordinal);
        Assert.Contains("heading.tabIndex = -1;", script, StringComparison.Ordinal);
        Assert.Contains("heading.focus({ preventScroll: true });", script, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WithoutAServiceAddressTheHomePageHasNoZipFormNoResultPanelAndNoNotice()
    {
        var html = await HomeHtmlAsync(null);
        var document = new HtmlParser().ParseDocument(html);

        Assert.Null(document.QuerySelector("main form"));
        Assert.Null(document.QuerySelector("[data-zip-results]"));
        Assert.DoesNotContain("ZIP lookup", html, StringComparison.Ordinal);
        Assert.NotNull(document.QuerySelector(".home-browse a"));
    }

    private static async Task<string> HomeHtmlAsync(string? publicApiUrl)
    {
        var release = ReleaseOf();
        var highlights = HomeHighlights.From(release, [], 0);
        return await new ReleaseRenderer(publicApiUrl, null).RenderHomeAsync(release, null, new ReleaseFacets([], []), highlights, CancellationToken.None);
    }

    private static async Task<AngleSharp.Dom.IElement> HomeAsync(PublicationRelease release, BreakLeader[] leaders, int officialCount = 0, ReleaseFacets? facets = null)
    {
        var highlights = HomeHighlights.From(release, [], officialCount) with { BreakLeaders = [.. leaders] };
        var html = await new ReleaseRenderer(null, null).RenderHomeAsync(release, null, facets ?? new ReleaseFacets([], []), highlights, CancellationToken.None);
        return new HtmlParser().ParseDocument(html).Body!;
    }

    private static BreakLeader Leader(string name = "Jane Doe", int breaks = 12, int cast = 40, string pagePath = "officials/doe.html", PublishedOfficeTerm[]? terms = null,
        PublishedChamber chamber = PublishedChamber.House) => new(chamber, PublishedPartyMajority.Democrat,
        new BoardMember(new OfficialListing(new PublishedOfficialProfile { Id = "doe", Name = name, Terms = terms ?? [Term("Democrat", "TX")] }, 0), cast, breaks, null, pagePath), 119);

    private static PublishedOfficeTerm Term(string? party, string? state, string? caucus = null, int startYear = 2025, PublishedOfficeKind kind = PublishedOfficeKind.Representative) => new()
    {
        Office = kind.ToString(),
        Kind = kind,
        State = state,
        Party = party,
        Caucus = caucus,
        Start = new DateOnly(startYear, 1, 3)
    };

    private static string Id(char letter) => new(letter, 32);

    private static PublicationRelease ReleaseOf(params PublishedRecordEntry[] entries) => new()
    {
        SchemaVersion = PublicationProtocol.SchemaVersion,
        ReleaseNumber = 3,
        PublishedAtUtc = Published,
        Withdrawals = [],
        Records = entries
    };

    private static PublishedRecordEntry Vote(char id, int yea, int nay, PublishedVoteAlignment? alignment, int day = 7) => Entry(PublishedRecordKind.Vote, id) with
    {
        Chamber = PublishedChamber.House,
        YeaCount = yea,
        NayCount = nay,
        Alignment = alignment,
        EventDate = new DateOnly(2026, 5, day)
    };

    private static PublishedRecordEntry Executive(char id, int day) => Entry(PublishedRecordKind.ExecutiveAction, id) with
    {
        DocumentType = PublishedExecutiveActionKind.Proclamation,
        EventDate = new DateOnly(2026, 4, day)
    };

    private static PublishedRecordEntry Entry(PublishedRecordKind kind, char id) => new()
    {
        Kind = kind,
        RecordId = Id(id),
        RevisionNumber = 1,
        Headline = $"Headline {id}",
        FirstPublishedAtUtc = Published,
        Officials = [],
        IssueIds = [],
        PolicyAreaIds = []
    };
}
