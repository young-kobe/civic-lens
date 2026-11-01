using AngleSharp.Html.Parser;
using CivicLens.Application.Publication;
using CivicLens.Host.Publication;
using CivicLens.Publication.Contracts;

namespace CivicLens.Tests.Host;

public sealed class PublishedHomeCardsTests
{
    private static readonly DateTimeOffset Published = new(2026, 5, 11, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task TheClosestVoteCardNamesTheDatesTheCountsAndTheSizeOfThePoolAndLinksTheVote()
    {
        var release = ReleaseOf(Vote('a', 216, 214, PublishedVoteAlignment.Split), Vote('b', 300, 100, PublishedVoteAlignment.PartiesAgree));

        var main = await HomeAsync(release, []);

        Assert.Contains("Closest vote, 5 May 2026 to 11 May 2026", main.QuerySelectorAll("h3").Select(heading => heading.TextContent));
        Assert.Contains("The Yea and Nay counts differ by 2 votes.", main.TextContent, StringComparison.Ordinal);
        Assert.Contains("closest of 2 substantive roll call votes with counts", main.TextContent, StringComparison.Ordinal);
        Assert.NotNull(main.QuerySelector($"a[href='records/{Id('a')}.html']"));
    }

    [Fact]
    public async Task WithNoVoteInTheWindowTheClosestVoteCardIsHiddenInsteadOfShowingAnOlderVoteOrAnEmptyCard()
    {
        var main = await HomeAsync(ReleaseOf(Vote('a', 216, 214, PublishedVoteAlignment.Split) with { EventDate = new DateOnly(2026, 1, 1) }), [Leader()]);

        Assert.DoesNotContain("Closest vote", main.TextContent, StringComparison.Ordinal);
        Assert.Contains("Voted against their party most often", main.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ATiedVoteSaysTheCountsAreEqualInsteadOfADifferenceOfZero()
    {
        var main = await HomeAsync(ReleaseOf(Vote('a', 50, 50, PublishedVoteAlignment.Mixed)), []);

        Assert.Contains("The Yea and Nay counts are equal.", main.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ThePicksPanelSaysAFixedRuleChoosesTheItemsAndLinksTheMethodOfTheChoices()
    {
        var main = await HomeAsync(ReleaseOf(Vote('a', 216, 214, PublishedVoteAlignment.Split)), []);

        Assert.Contains("A fixed rule chooses these items from the published records. The same records on the same release day give the same choices.", main.TextContent, StringComparison.Ordinal);
        Assert.Equal("How these choices are made", main.QuerySelector("a[href='method.html#home-picks']")!.TextContent);
    }

    [Fact]
    public async Task TheBreakCardNamesWhoWithPartyAndStateAndWhatWasCountedLinksTheOfficialAndStatesTheMinimum()
    {
        var main = await HomeAsync(ReleaseOf(), [Leader()]);

        var item = main.QuerySelector("li")!;
        Assert.Equal("Jane Doe (D, Texas) sided against most House Democrats on 12 of 40 votes where the parties split.", item.TextContent);
        Assert.Equal("officials/doe.html", item.QuerySelector("a")!.GetAttribute("href"));
        Assert.Contains("A chamber and party with no line has no member with vote figures on this site who meets these rules.", main.TextContent, StringComparison.Ordinal);
        Assert.Contains("at least 25 split votes", main.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WithNoVoteAndNoLeaderTheWholePicksPanelIsHiddenInsteadOfShowingAnEmptyPanel()
    {
        var main = await HomeAsync(ReleaseOf(), []);

        Assert.DoesNotContain("Chosen by a fixed rule", main.TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain("Newest executive actions", main.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheBreakCardUsesThePartyOfTheCountedChamberSoAMemberWhoChangedPartyIsNotLabeledWithTheNewParty()
    {
        var main = await HomeAsync(ReleaseOf(), [Leader([Term("Democrat", "TX", startYear: 2021), Term("Republican", "FL", startYear: 2025)])]);

        Assert.Equal("Jane Doe (D, Texas) sided against most House Democrats on 12 of 40 votes where the parties split.", main.QuerySelector("li")!.TextContent);
    }

    [Fact]
    public async Task ALaterExecutiveTermOfAnotherPartyDoesNotChangeTheLabelOfAMemberOfTheHouse()
    {
        var main = await HomeAsync(ReleaseOf(), [Leader([Term("Democrat", "TX", startYear: 2021), Term("Republican", null, startYear: 2025, kind: PublishedOfficeKind.Executive)])]);

        Assert.Equal("Jane Doe (D, Texas) sided against most House Democrats on 12 of 40 votes where the parties split.", main.QuerySelector("li")!.TextContent);
    }

    [Theory]
    [InlineData("Independent", "Democrat", "ME", "Jane Doe (I, Maine) ")]
    [InlineData("Democrat", null, null, "Jane Doe (D) ")]
    [InlineData(null, "Democrat", "TX", "Jane Doe (Texas) ")]
    [InlineData("Democrat", null, "DC", "Jane Doe (D, District of Columbia) ")]
    public async Task TheBreakCardShowsOnlyThePartyLetterAndStateNameThatTheTermHolds(string? party, string? caucus, string? state, string expectedStart)
    {
        var main = await HomeAsync(ReleaseOf(), [Leader([Term(party, state, caucus)])]);

        Assert.StartsWith(expectedStart + "sided against most House Democrats", main.QuerySelector("li")!.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheBreakCardLeavesOutThePartyAndStateItDoesNotHoldInsteadOfPrintingEmptyBrackets()
    {
        var main = await HomeAsync(ReleaseOf(), [Leader(terms: [])]);

        Assert.Equal("Jane Doe sided against most House Democrats on 12 of 40 votes where the parties split.", main.QuerySelector("li")!.TextContent);
    }

    [Fact]
    public async Task TheExecutiveActionCardListsTheNewestThreeWithTheirDateAndSaysHowManyExist()
    {
        var release = ReleaseOf(Executive('a', 1), Executive('b', 2), Executive('c', 3), Executive('d', 4));

        var main = await HomeAsync(release, []);

        var table = main.QuerySelector("table")!;
        Assert.Equal([Id('d'), Id('c'), Id('b')], table.QuerySelectorAll("a.cell-title").Select(link => link.GetAttribute("href")!.Split('/')[1][..32]));
        Assert.Contains("Chosen by a fixed rule: the 3 newest of 4 executive actions on this site.", main.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheExecutiveActionAsideSaysTheOnlyActionForOneAndTheCardIsHiddenForNone()
    {
        var one = await HomeAsync(ReleaseOf(Executive('a', 1)), []);
        var none = await HomeAsync(ReleaseOf(), []);

        Assert.Contains("Chosen by a fixed rule: the only executive action on this site.", one.TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain("Newest executive actions", none.TextContent, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(PublishedVoteAlignment.Split, "Split vote", "A split vote is a substantive roll call vote where most Democrats and most Republicans who cast Yea or Nay voted on opposite sides.")]
    [InlineData(PublishedVoteAlignment.PartiesAgree, "Both parties agreed", "A vote where both parties agreed is a substantive roll call vote where most Democrats and most Republicans who cast Yea or Nay voted the same way.")]
    public async Task AVoteInAListShowsItsPartyBadgeWithItsMeaningAndItsCounts(PublishedVoteAlignment alignment, string badge, string meaning)
    {
        var main = await HomeAsync(ReleaseOf(Vote('a', 216, 214, alignment)), []);

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

        var main = await HomeAsync(ReleaseOf(candidate, mixed), []);

        var cells = main.QuerySelectorAll("tbody tr").Select(row => row.QuerySelector("td")!).ToList();
        Assert.All(cells, cell => Assert.Single(cell.QuerySelectorAll(".badge")));
        Assert.DoesNotContain("Casts of all members of the chamber", cells.Single(cell => cell.QuerySelector($"a[href='records/{Id('a')}.html']") is not null).TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WithFewerActionsThanTheCardHoldsTheAsideSaysAllOfThemAreShown()
    {
        var main = await HomeAsync(ReleaseOf(Executive('a', 1), Executive('b', 2)), []);

        Assert.Contains("Chosen by a fixed rule: all 2 executive actions on this site, newest first.", main.TextContent, StringComparison.Ordinal);
    }

    private static async Task<AngleSharp.Dom.IElement> HomeAsync(PublicationRelease release, BreakLeader[] leaders)
    {
        var highlights = HomeHighlights.From(release, []) with { BreakLeaders = [.. leaders] };
        var html = await new ReleaseRenderer(null, null).RenderHomeAsync(release, null, new ReleaseFacets([], []), highlights, CancellationToken.None);
        return new HtmlParser().ParseDocument(html).QuerySelector("main")!;
    }

    private static BreakLeader Leader(PublishedOfficeTerm[]? terms = null) => new(PublishedChamber.House, PublishedPartyMajority.Democrat,
        new OfficialListing(new PublishedOfficialProfile { Id = "doe", Name = "Jane Doe", Terms = terms ?? [Term("Democrat", "TX")] }, 0), 40, 12);

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

    private static PublishedRecordEntry Vote(char id, int yea, int nay, PublishedVoteAlignment? alignment) => Entry(PublishedRecordKind.Vote, id) with
    {
        YeaCount = yea,
        NayCount = nay,
        Alignment = alignment,
        EventDate = new DateOnly(2026, 5, 7)
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
