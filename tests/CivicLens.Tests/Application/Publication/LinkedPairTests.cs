using CivicLens.Application.Publication;
using CivicLens.Publication.Contracts;

namespace CivicLens.Tests.Application.Publication;

public sealed class LinkedPairTests
{
    private static readonly string StatementId = new('1', 64);
    private static readonly string BillId = new('2', 64);
    private static readonly string VoteId = new('3', 64);

    [Fact]
    public void PairsFollowTheStatementDayNewestFirstWithTheTitleAndTheCastOfTheOfficial()
    {
        var old = Link('a', BillId, PublishedRecordKind.Action, new DateOnly(2026, 2, 1));
        var recent = Link('b', VoteId, PublishedRecordKind.Vote, new DateOnly(2026, 3, 1));

        var pairs = LinkedPair.In(119, [old, recent], Release(old, recent), [Vote("Nay")]);

        Assert.Equal([VoteId, BillId], pairs.Select(pair => pair.TargetRecordId));
        Assert.Equal(("Nay", "Official bill title"), (pairs[0].Cast, pairs[1].TargetTitle));
        Assert.Equal((new DateOnly(2026, 3, 1), "Statement headline", PublishedRelationshipType.StatementAboutVote),
            (pairs[0].StatementDay, pairs[0].StatementHeadline, pairs[0].Type));
    }

    [Fact]
    public void AVoteWithoutACastRowOfTheOfficialHasNoCast()
    {
        var link = Link('a', VoteId, PublishedRecordKind.Vote, new DateOnly(2026, 3, 1));

        var pairs = LinkedPair.In(119, [link], Release(link), [Vote("Yea") with { RecordId = new string('9', 64) }]);

        Assert.Null(Assert.Single(pairs).Cast);
    }

    [Fact]
    public void ABillNeverHasACastAndShowsItsHeadlineWhenItsEntryHasNoOfficialTitle()
    {
        var link = Link('a', BillId, PublishedRecordKind.Action, new DateOnly(2026, 3, 1));
        var untitled = Release(link);
        untitled[(PublishedRecordKind.Action, BillId)] = untitled[(PublishedRecordKind.Action, BillId)] with { BillTitle = null };

        var pair = Assert.Single(LinkedPair.In(119, [link], untitled, [Vote("Yea") with { RecordId = BillId }]));

        Assert.Equal(("Action headline", null), (pair.TargetTitle, pair.Cast));
    }

    [Fact]
    public void AStatementOfAnotherCongressIsNotInThePairsOfThisCongress()
    {
        var link = Link('a', BillId, PublishedRecordKind.Action, new DateOnly(2024, 3, 1));

        Assert.Empty(LinkedPair.In(119, [link], Release(link), []));
    }

    [Fact]
    public void ALinkWhoseRecordIsNotInTheReleaseIsLeftOutSoThePageNeverCrashes()
    {
        var link = Link('a', BillId, PublishedRecordKind.Action, new DateOnly(2026, 3, 1));
        var noTarget = Release(link);
        noTarget.Remove((PublishedRecordKind.Action, BillId));
        var noStatement = Release(link);
        noStatement.Remove((PublishedRecordKind.Statement, StatementId));

        Assert.Empty(LinkedPair.In(119, [link], noTarget, []));
        Assert.Empty(LinkedPair.In(119, [link], noStatement, []));
    }

    [Fact]
    public void OnlyRelationshipEntriesMakeAPair()
    {
        var statement = Entry(PublishedRecordKind.Statement, StatementId, new DateOnly(2026, 3, 1));

        Assert.Empty(LinkedPair.In(119, [statement], Release(), []));
    }

    private static Dictionary<(PublishedRecordKind Kind, string RecordId), PublishedRecordEntry> Release(params PublishedRecordEntry[] links) => new[]
    {
        Entry(PublishedRecordKind.Statement, StatementId, new DateOnly(2026, 1, 1)) with { Headline = "Statement headline" },
        Entry(PublishedRecordKind.Action, BillId, new DateOnly(2026, 1, 1)) with { Headline = "Action headline", BillTitle = "Official bill title" },
        Entry(PublishedRecordKind.Vote, VoteId, new DateOnly(2026, 1, 1)) with { Headline = "Vote headline" }
    }.Concat(links).ToDictionary(entry => (entry.Kind, entry.RecordId));

    private static PublishedRecordEntry Link(char id, string targetId, PublishedRecordKind targetKind, DateOnly statementDay) =>
        Entry(PublishedRecordKind.Relationship, new string(id, 64), statementDay) with
        {
            LinkType = targetKind == PublishedRecordKind.Vote ? PublishedRelationshipType.StatementAboutVote : PublishedRelationshipType.StatesPositionOn,
            StatementRecordId = StatementId,
            TargetKind = targetKind,
            TargetRecordId = targetId
        };

    private static PublishedRecordEntry Entry(PublishedRecordKind kind, string recordId, DateOnly day) => new()
    {
        Kind = kind,
        RecordId = recordId,
        RevisionNumber = 1,
        Headline = "Headline",
        FirstPublishedAtUtc = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero),
        Officials = [new() { Id = "mayor", Name = "Mayor Example" }],
        IssueIds = [],
        PolicyAreaIds = [],
        EventDate = day
    };

    private static PublishedOfficialVote Vote(string cast) => new()
    {
        RecordId = VoteId,
        VotedOn = new DateOnly(2026, 3, 2),
        Chamber = PublishedChamber.House,
        Congress = 119,
        Session = 1,
        RollCallNumber = 100,
        Cast = cast,
        Result = "Passed",
        Totals = new() { Yea = 218, Nay = 210, Present = 1, NotVoting = 5 },
        SplitVote = false,
        Party = "Republican",
        Headline = "Vote headline",
        Kind = PublishedVoteKind.Passage,
        IssueIds = []
    };
}
