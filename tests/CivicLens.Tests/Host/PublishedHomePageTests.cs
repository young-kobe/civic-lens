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
    public async Task TheLatestListHoldsTheFourNewestVotesAndTheThreeNewestActionsNewestFirstWithTheirDateAndALink()
    {
        var release = ReleaseOf(Vote('a', 1, 1, null, 1), Vote('b', 1, 1, null, 2), Vote('c', 1, 1, null, 3), Vote('d', 1, 1, null, 4), Vote('e', 1, 1, null, 5),
            Executive('f', 1), Executive('g', 2), Executive('h', 3), Executive('i', 4));

        var main = await HomeAsync(release, []);

        var section = main.QuerySelector("section#latest")!;
        Assert.Equal("Latest from the record", section.QuerySelector("h2")!.TextContent);
        var columns = section.QuerySelectorAll(".latest-columns > div").ToList();
        Assert.Equal(["Votes", "Executive actions"], columns.Select(column => column.QuerySelector("h3")!.TextContent));
        Assert.Equal([Id('e'), Id('d'), Id('c'), Id('b')], columns[0].QuerySelectorAll("li a").Select(link => link.GetAttribute("href")!.Split('/')[1][..32]));
        Assert.Equal([Id('i'), Id('h'), Id('g')], columns[1].QuerySelectorAll("li a").Select(link => link.GetAttribute("href")!.Split('/')[1][..32]));
        Assert.Equal(["Headline e", "5 May 2026"], columns[0].QuerySelector("li a")!.Children.Select(child => child.TextContent));
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
    public async Task TheIssueTilesLinkEachIssuePageAndDrawTheConfiguredIconOrTheNeutralDefaultAndSkipAnIssueWithNoPage()
    {
        ReleaseFacet Facet(string id, string? icon) => new(FacetKind.Issue, id, $"Name {id}", [], icon);
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
    [InlineData(PublishedVoteAlignment.Split, "Split vote", "A split vote is a substantive roll call vote where most Democrats and most Republicans who cast Yea or Nay voted on opposite sides.")]
    [InlineData(PublishedVoteAlignment.PartiesAgree, "Both parties agreed", "A vote where both parties agreed is a substantive roll call vote where most Democrats and most Republicans who cast Yea or Nay voted the same way.")]
    public async Task AVoteInAListShowsItsPartyBadgeWithItsMeaningAndItsCounts(PublishedVoteAlignment alignment, string badge, string meaning)
    {
        var main = await AllRecordsAsync(ReleaseOf(Vote('a', 216, 214, alignment)));

        var cell = main.QuerySelector("tbody td")!;
        Assert.Equal(["Vote", badge], cell.QuerySelectorAll(".badge").Select(item => item.TextContent));
        Assert.Contains(meaning, cell.TextContent, StringComparison.Ordinal);
        Assert.Contains("Casts of all members of the chamber who voted Yea or Nay: Yea 216, Nay 214.", cell.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ACandidateVoteAndAMixedVoteGetNoPartyBadgeAndACandidateVoteShowsNoCounts()
    {
        var candidate = Vote('a', 0, 0, null) with { YeaCount = null, NayCount = null };
        var mixed = Vote('b', 50, 50, PublishedVoteAlignment.Mixed);

        var main = await AllRecordsAsync(ReleaseOf(candidate, mixed));

        var cells = main.QuerySelectorAll("tbody tr").Select(row => row.QuerySelector("td")!).ToList();
        Assert.All(cells, cell => Assert.Single(cell.QuerySelectorAll(".badge")));
        Assert.DoesNotContain("Casts of all members of the chamber", cells.Single(cell => cell.QuerySelector($"a[href='records/{Id('a')}.html']") is not null).TextContent, StringComparison.Ordinal);
    }

    private static async Task<AngleSharp.Dom.IElement> AllRecordsAsync(PublicationRelease release) =>
        new HtmlParser().ParseDocument(await new ReleaseRenderer(null, null).RenderAllRecordsAsync(release, 1, CancellationToken.None)).QuerySelector("main")!;

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
