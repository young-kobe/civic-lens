using System.Text.Json;
using CivicLens.Publication.Contracts;

namespace CivicLens.Tests.Publication.Contracts;

public sealed class PublishedOfficialVotesTests
{
    [Fact]
    public void TheVoteJsonMatchesThePinnedSchema()
    {
        var file = PublishedOfficialVotes.Of([Vote('b', new DateOnly(2026, 2, 1), 9)]);

        Assert.Equal("""{"votes":[{"recordId":"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb","votedOn":"2026-02-01","congress":119,"session":1,"rollCallNumber":9,"billType":"hr","billNumber":7,"cast":"Yea","headline":"Vote b"}]}""",
            JsonSerializer.Serialize(file, PublicationProtocol.JsonOptions));
    }

    [Fact]
    public void AFileHoldsItsVotesNewestFirst()
    {
        var file = PublishedOfficialVotes.Of([Vote('a', new DateOnly(2026, 1, 1), 5), Vote('b', new DateOnly(2026, 2, 1), 9)]);

        Assert.Equal([new string('b', 64), new string('a', 64)], file.Votes.Select(vote => vote.RecordId));
    }

    [Fact]
    public void AFullCongressOfMaximumLengthRowsStaysUnderTheByteCapSoTheFileCanAlwaysBeReadBack()
    {
        var long8 = new string('é', PublishedOfficialVote.MaximumHeadlineLength);
        var rows = Enumerable.Range(0, PublishedOfficialVotes.MaximumVotes)
            .Select(index => Vote('a', new DateOnly(2026, 1, 1), index + 1) with
            {
                RecordId = index.ToString("x64"),
                Headline = long8,
                Cast = new string('é', PublishedOfficialVote.MaximumCastLength),
                BillType = new string('z', PublishedOfficialVote.MaximumBillTypeLength)
            });

        var json = JsonSerializer.SerializeToUtf8Bytes(PublishedOfficialVotes.Of(rows), PublicationProtocol.JsonOptions);

        Assert.True(json.Length < PublishedOfficialVotes.MaximumBytes);
    }

    [Fact]
    public void MoreVotesThanTheCapAreRefused()
    {
        var votes = Enumerable.Range(0, PublishedOfficialVotes.MaximumVotes + 1)
            .Select(index => Vote('a', new DateOnly(2026, 1, 1), 1) with { RecordId = index.ToString("x64") });

        Assert.Throws<InvalidDataException>(() => PublishedOfficialVotes.Of(votes));
    }

    [Fact]
    public void VotesOfTwoCongressesInOneFileAreRefusedBecauseThePathNamesOneCongress()
    {
        Assert.Throws<InvalidDataException>(() => PublishedOfficialVotes.Of([Vote('a', new DateOnly(2026, 1, 1), 1), Vote('b', new DateOnly(2026, 1, 1), 2) with { Congress = 118 }]));
    }

    [Fact]
    public void ADuplicateRecordIsRefusedSoAReplacedVoteCannotAppearTwice()
    {
        Assert.Throws<InvalidDataException>(() => PublishedOfficialVotes.Of([Vote('a', new DateOnly(2026, 1, 1), 1), Vote('a', new DateOnly(2026, 1, 1), 1)]));
    }

    [Fact]
    public void ABillTypeWithoutANumberIsRefused()
    {
        Assert.Throws<InvalidDataException>(() => PublishedOfficialVotes.Of([Vote('a', new DateOnly(2026, 1, 1), 1) with { BillNumber = null }]));
    }

    private static PublishedOfficialVote Vote(char id, DateOnly date, int roll) => new()
    {
        RecordId = new string(id, 64),
        VotedOn = date,
        Congress = 119,
        Session = 1,
        RollCallNumber = roll,
        BillType = "hr",
        BillNumber = 7,
        Cast = "Yea",
        Headline = $"Vote {id}"
    };
}
