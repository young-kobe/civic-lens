using CivicLens.Application.Publication;
using CivicLens.Publication.Contracts;

namespace CivicLens.Tests.Application.Publication;

public sealed class MissedVotesTests
{
    [Fact]
    public void OnlyNotVotingIsMissedBecausePresentAndACandidateNameAreCasts()
    {
        var votes = new[] { Vote(1, "Yea"), Vote(2, PublishedOfficialVote.NotVoting), Vote(3, "Present"), Vote(4, "Pelosi"), Vote(5, PublishedOfficialVote.NotVoting) };

        var missed = Assert.Single(MissedVotes.Of(votes));

        Assert.Equal((2, 5), (missed.Missed, missed.Held));
    }

    [Fact]
    public void AMemberWhoMovedChambersGetsOneEntryForEachChamberAndAChamberWithNoRowsGetsNone()
    {
        var votes = new[] { Vote(1, "Yea"), Vote(2, PublishedOfficialVote.NotVoting, PublishedChamber.Senate), Vote(3, "Nay", PublishedChamber.Senate) };

        var all = MissedVotes.Of(votes);

        Assert.Equal([(PublishedChamber.House, 0, 1), (PublishedChamber.Senate, 1, 2)], all.Select(entry => (entry.Chamber, entry.Missed, entry.Held)));
        Assert.Equal([PublishedChamber.House], MissedVotes.Of(votes.Take(1)).Select(entry => entry.Chamber));
        Assert.Empty(MissedVotes.Of([]));
    }

    [Fact]
    public void TheDaysAreTheFirstAndLastDayOfTheRowsOfTheChamberWhateverTheirOrder()
    {
        var votes = new[] { Vote(1, "Yea") with { VotedOn = new(2026, 3, 1) }, Vote(2, "Yea") with { VotedOn = new(2025, 1, 3) },
            Vote(3, "Yea") with { VotedOn = new(2026, 10, 20) }, Vote(4, "Yea", PublishedChamber.Senate) with { VotedOn = new(2027, 1, 1) } };

        var house = MissedVotes.Of(votes).First();

        Assert.Equal((new DateOnly(2025, 1, 3), new DateOnly(2026, 10, 20)), (house.First, house.Last));
    }

    [Theory]
    [InlineData(4, false)]
    [InlineData(5, true)]
    public void TheShareShowsOnlyAtTheThresholdOfHeldRollCalls(int held, bool showsShare)
    {
        var votes = Enumerable.Range(0, held).Select(index => Vote(index, index == 0 ? PublishedOfficialVote.NotVoting : "Yea"));

        var share = Assert.Single(MissedVotes.Of(votes)).Share;

        Assert.Equal(showsShare ? 1.0 / held : null, share);
    }

    internal static PublishedOfficialVote Vote(int id, string cast, PublishedChamber chamber = PublishedChamber.House) => new()
    {
        RecordId = id.ToString("x64"),
        VotedOn = new DateOnly(2026, 1, 1),
        Chamber = chamber,
        Congress = 119,
        Session = 1,
        RollCallNumber = id + 1,
        Cast = cast,
        Result = "Passed",
        Party = "Republican",
        Headline = $"Vote {id}",
        Kind = PublishedVoteKind.Passage,
        IssueIds = []
    };
}
