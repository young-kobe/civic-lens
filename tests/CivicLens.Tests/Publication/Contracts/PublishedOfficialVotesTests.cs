using System.Text.Json;
using CivicLens.Publication.Contracts;

namespace CivicLens.Tests.Publication.Contracts;

public sealed class PublishedOfficialVotesTests
{
    [Fact]
    public void TheVoteJsonMatchesThePinnedSchema()
    {
        var file = PublishedOfficialVotes.Of([Vote('b', new DateOnly(2026, 2, 1), 9)]);

        Assert.Equal("""{"votes":[{"recordId":"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb","votedOn":"2026-02-01","chamber":"house","congress":119,"session":1,"rollCallNumber":9,"billType":"hr","billNumber":7,"cast":"Yea","headline":"Vote b","kind":"passage","issueIds":["housing"]}]}""",
            JsonSerializer.Serialize(file, PublicationProtocol.JsonOptions));
    }

    [Fact]
    public void AFileHoldsItsVotesNewestFirst()
    {
        var file = PublishedOfficialVotes.Of([Vote('a', new DateOnly(2026, 1, 1), 5), Vote('b', new DateOnly(2026, 2, 1), 9)]);

        Assert.Equal([new string('b', 64), new string('a', 64)], file.Votes.Select(vote => vote.RecordId));
    }

    [Fact]
    public void ARollNumberOfOneChamberNeverOrdersAmongTheRollNumbersOfTheOtherOnTheSameDay()
    {
        var day = new DateOnly(2026, 1, 1);
        var file = PublishedOfficialVotes.Of([Vote('a', day, 50) with { Chamber = PublishedChamber.Senate }, Vote('b', day, 10), Vote('c', day, 20)]);

        Assert.Equal([new string('c', 64), new string('b', 64), new string('a', 64)], file.Votes.Select(vote => vote.RecordId));
    }

    [Fact]
    public void AnUndefinedChamberIsRefused()
    {
        Assert.Throws<InvalidDataException>(() => PublishedOfficialVotes.Of([Vote('a', new DateOnly(2026, 1, 1), 1) with { Chamber = (PublishedChamber)9 }]));
    }

    [Fact]
    public void AFullCongressOfMaximumLengthRowsStaysUnderTheByteCapSoTheFileCanAlwaysBeReadBack()
    {
        var long8 = new string('€', PublishedVote.MaximumHeadlineLength);
        string[] issueIds = [.. Enumerable.Range(0, PublishedRecordEntry.MaximumFilterValues)
            .Select(issue => issue.ToString("x2") + new string('€', PublishedRecordEntry.MaximumFilterValueLength - 2))];
        var rows = Enumerable.Range(0, PublishedOfficialVotes.MaximumVotes)
            .Select(index => Vote('a', new DateOnly(2026, 1, 1), index + 1) with
            {
                RecordId = index.ToString("x64"),
                Headline = long8,
                Cast = new string('€', PublishedVote.MaximumCastLength),
                BillType = new string('z', PublishedVote.MaximumBillTypeLength),
                IssueIds = issueIds
            });

        var json = JsonSerializer.SerializeToUtf8Bytes(PublishedOfficialVotes.Of(rows), PublicationProtocol.JsonOptions);

        Assert.True(json.Length < PublishedOfficialVotes.MaximumBytes);
    }

    [Fact]
    public void AnUndefinedKindIsRefused()
    {
        Assert.Throws<InvalidDataException>(() => PublishedOfficialVotes.Of([Vote('a', new DateOnly(2026, 1, 1), 1) with { Kind = (PublishedVoteKind)99 }]));
    }

    [Fact]
    public void TheRowKeepsTheKindAndTheTrackedIssuesOfItsVote()
    {
        var file = PublishedOfficialVotes.Of([Vote('a', new DateOnly(2026, 1, 1), 1) with { Kind = PublishedVoteKind.Cloture, IssueIds = ["housing", "health"] }]);

        var row = JsonSerializer.Deserialize<PublishedOfficialVotes>(JsonSerializer.Serialize(file, PublicationProtocol.JsonOptions), PublicationProtocol.JsonOptions)!.Votes.Single();

        Assert.Equal(PublishedVoteKind.Cloture, row.Kind);
        Assert.Equal(["housing", "health"], row.IssueIds);
    }

    [Theory]
    [MemberData(nameof(InvalidIssueIds))]
    public void IssueIdsThatTheManifestEntryWouldRefuseAreRefusedOnTheRow(string[] issueIds)
    {
        Assert.Throws<InvalidDataException>(() => PublishedOfficialVotes.Of([Vote('a', new DateOnly(2026, 1, 1), 1) with { IssueIds = issueIds }]));
    }

    public static TheoryData<string[]> InvalidIssueIds() => new()
    {
        { ["housing", "housing"] },
        { [" "] },
        { [new string('x', PublishedRecordEntry.MaximumFilterValueLength + 1)] },
        { [.. Enumerable.Range(0, PublishedRecordEntry.MaximumFilterValues + 1).Select(index => $"issue-{index}")] }
    };

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
        Chamber = PublishedChamber.House,
        Congress = 119,
        Session = 1,
        RollCallNumber = roll,
        BillType = "hr",
        BillNumber = 7,
        Cast = "Yea",
        Headline = $"Vote {id}",
        Kind = PublishedVoteKind.Passage,
        IssueIds = ["housing"]
    };
}
