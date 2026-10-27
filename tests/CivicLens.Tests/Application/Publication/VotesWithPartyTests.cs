using CivicLens.Application.Publication;
using CivicLens.Publication.Contracts;

namespace CivicLens.Tests.Application.Publication;

public sealed class VotesWithPartyTests
{
    [Fact]
    public void OnlyACastOfYeaOrNayCountsAndAyeAndNoMapToThem()
    {
        var votes = new[] { Vote(1, "Aye"), Vote(2, "No"), Vote(3, "Yea"), Vote(4, "Present"), Vote(5, PublishedOfficialVote.NotVoting), Vote(6, "Pelosi") };

        var result = Assert.Single(VotesWithParty.Of(votes));

        Assert.Equal((2, 1, 3), (result.Matched, result.Breaks.Length, result.NotCast));
    }

    [Fact]
    public void ACastAgainstTheMajorityOfTheOwnPartyIsABreakNewestFirst()
    {
        var votes = new[] { Vote(1, "Nay") with { VotedOn = new(2026, 1, 2) }, Vote(2, "Nay") with { VotedOn = new(2026, 3, 1) },
            Vote(3, "Nay") with { VotedOn = new(2026, 3, 1), RollCallNumber = 90 }, Vote(4, "Yea") };

        var result = Assert.Single(VotesWithParty.Of(votes));

        Assert.Equal([90, 3, 2], result.Breaks.Select(vote => vote.RollCallNumber));
        Assert.Equal(1, result.Matched);
    }

    [Fact]
    public void AVoteThatIsNotSplitCountsNowhere()
    {
        var votes = new[] { Vote(1, "Nay") with { SplitVote = false }, Vote(2, "Nay") with { SplitVote = false }, Vote(3, "Yea") };

        var result = Assert.Single(VotesWithParty.Of(votes));

        Assert.Equal((1, 0, 0), (result.Matched, result.Breaks.Length, result.NotCast));
    }

    [Fact]
    public void AChamberWithNoSplitVoteStillGetsAnEntryWithNoCasts()
    {
        var result = Assert.Single(VotesWithParty.Of([Vote(1, "Yea") with { SplitVote = false }]));

        Assert.Equal((0, null), (result.Cast, result.Share));
    }

    [Fact]
    public void AMemberWhoMovedChambersGetsOneEntryForEachChamber()
    {
        var votes = new[] { Vote(1, "Yea", PublishedChamber.Senate), Vote(2, "Nay"), Vote(3, "Yea") };

        var all = VotesWithParty.Of(votes);

        Assert.Equal([(PublishedChamber.House, 1, 1), (PublishedChamber.Senate, 1, 0)], all.Select(entry => (entry.Chamber, entry.Matched, entry.Breaks.Length)));
    }

    [Theory]
    [InlineData("Independent")]
    [InlineData("Libertarian")]
    public void APartyThatIsNeitherDemocratNorRepublicanGetsNoEntry(string party)
    {
        Assert.Empty(VotesWithParty.Of([Vote(1, "Yea") with { Party = party }]));
    }

    [Theory]
    [InlineData(4, false)]
    [InlineData(5, true)]
    public void TheShareShowsOnlyAtTheThresholdOfCastSplitVotes(int cast, bool showsShare)
    {
        var votes = Enumerable.Range(0, cast).Select(index => Vote(index, index == 0 ? "Nay" : "Yea"));

        var share = Assert.Single(VotesWithParty.Of(votes)).Share;

        Assert.Equal(showsShare ? (cast - 1.0) / cast : null, share);
    }

    private static PublishedOfficialVote Vote(int id, string cast, PublishedChamber chamber = PublishedChamber.House) => new()
    {
        RecordId = id.ToString("x64"),
        VotedOn = new DateOnly(2026, 1, 1),
        Chamber = chamber,
        Congress = 119,
        Session = 1,
        RollCallNumber = id + 1,
        Cast = cast,
        Result = "Passed",
        Totals = new() { Yea = 218, Nay = 210, Present = 1, NotVoting = 5 },
        SplitVote = true,
        Party = PublishedPartyMajority.Democrat,
        PartyMajority = PublishedPartyMajority.Yea,
        Headline = $"Vote {id}",
        Kind = PublishedVoteKind.Passage,
        IssueIds = []
    };
}
