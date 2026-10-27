using CivicLens.Application.Publication;
using CivicLens.Publication.Contracts;

namespace CivicLens.Tests.Application.Publication;

public sealed class TalkAndActionProfileTests
{
    private static readonly DateOnly InCongress = new(2026, 1, 1);

    [Fact]
    public void OnlySubstantiveVoteKindsCountAndTheOthersAreCountedAsProcedural()
    {
        PublishedVoteKind[] substantive =
            [PublishedVoteKind.Passage, PublishedVoteKind.Suspension, PublishedVoteKind.Amendment, PublishedVoteKind.ConferenceReport,
                PublishedVoteKind.VetoOverride, PublishedVoteKind.Nomination];
        var procedural = Enum.GetValues<PublishedVoteKind>().Except(substantive).ToArray();
        var votes = Enumerable.Concat(substantive, procedural).Select((kind, index) => Vote(index, kind, ["housing"]));

        var profile = TalkAndActionProfile.Of(119, [], votes);

        Assert.Equal(substantive.Length, profile.Votes.CountOf("housing"));
        Assert.Equal(procedural.Length, profile.ProceduralVotes);
        Assert.Equal(substantive.Order(), Enum.GetValues<PublishedVoteKind>().Where(kind => kind.IsSubstantive()).Order());
    }

    [Fact]
    public void ANotVotingCastLeavesTheVotesColumnWhilePresentAndACandidateNameStayIn()
    {
        var votes = new[] { Vote(1, PublishedVoteKind.Passage, ["housing"]), Vote(2, PublishedVoteKind.Passage, ["housing"]) with { Cast = PublishedOfficialVote.NotVoting },
            Vote(3, PublishedVoteKind.Passage, ["housing"]) with { Cast = "Present" }, Vote(4, PublishedVoteKind.Cloture, ["housing"]) with { Cast = PublishedOfficialVote.NotVoting },
            Vote(5, PublishedVoteKind.Passage, ["housing"]) with { Cast = "Pelosi" } };

        var profile = TalkAndActionProfile.Of(119, [], votes);

        Assert.Equal((3, 1), (profile.Votes.CountOf("housing"), profile.ProceduralVotes));
    }

    [Fact]
    public void OnlyVoteRowsOfTheCurrentCongressCountAsMissedOrHeld()
    {
        var votes = new[] { Vote(1, PublishedVoteKind.Passage, []) with { Cast = PublishedOfficialVote.NotVoting }, Vote(2, PublishedVoteKind.Passage, []) with { Congress = 118, Cast = PublishedOfficialVote.NotVoting },
            Vote(3, PublishedVoteKind.Cloture, []) with { Cast = PublishedOfficialVote.NotVoting } };

        var missed = Assert.Single(TalkAndActionProfile.Of(119, [], votes).MissedByChamber);

        Assert.Equal((2, 2), (missed.Missed, missed.Held));
    }

    [Fact]
    public void OnlyStatementsAndActionsCountAsTalkAndSponsorshipSoOtherEntriesNamingTheOfficialAreIgnored()
    {
        PublishedRecordEntry[] entries =
        [
            Entry(1, PublishedRecordKind.Statement, InCongress, ["housing"]),
            Entry(2, PublishedRecordKind.Action, InCongress, ["housing"]),
            Entry(3, PublishedRecordKind.Relationship, InCongress, ["housing"]),
            Entry(4, PublishedRecordKind.Brief, InCongress, ["housing"]),
            Entry(5, PublishedRecordKind.DocumentChange, InCongress, ["housing"]),
            Entry(6, PublishedRecordKind.Vote, InCongress, ["housing"])
        ];

        var profile = TalkAndActionProfile.Of(119, entries, []);

        Assert.Equal((1, 1, 0), (profile.Statements.WithIssue, profile.Sponsorships.WithIssue, profile.Votes.WithIssue));
    }

    [Fact]
    public void AStatementOrASponsorshipOutsideTheCurrentCongressDoesNotCountButTheEdgeDaysDo()
    {
        PublishedRecordEntry[] entries =
        [
            Entry(1, PublishedRecordKind.Statement, new DateOnly(2025, 1, 3), ["housing"]),
            Entry(2, PublishedRecordKind.Statement, new DateOnly(2027, 1, 2), ["housing"]),
            Entry(3, PublishedRecordKind.Statement, new DateOnly(2025, 1, 2), ["housing"]),
            Entry(4, PublishedRecordKind.Statement, new DateOnly(2027, 1, 3), ["housing"]),
            Entry(5, PublishedRecordKind.Action, new DateOnly(2024, 12, 31), ["housing"]),
            Entry(6, PublishedRecordKind.Action, new DateOnly(2027, 1, 3), ["housing"])
        ];

        var profile = TalkAndActionProfile.Of(119, entries, []);

        Assert.Equal((2, 0), (profile.Statements.WithIssue, profile.Sponsorships.WithIssue));
    }

    [Fact]
    public void AnEntryWithNoEventDateCountsInTheCongressOfItsFirstPublicationDay()
    {
        var entry = Entry(1, PublishedRecordKind.Statement, InCongress, ["housing"]) with
        {
            EventDate = null,
            FirstPublishedAtUtc = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero)
        };

        Assert.Equal(1, TalkAndActionProfile.Of(119, [entry], []).Statements.WithIssue);
        Assert.Equal(0, TalkAndActionProfile.Of(118, [entry], []).Statements.WithIssue);
    }

    [Fact]
    public void ARecordWithSeveralIssuesCountsOncePerIssueAndTheShareDividesByRecordsWithAnIssueSoSharesCanPassOneHundredPercent()
    {
        var entries = Enumerable.Range(0, 5).Select(index => Entry(index, PublishedRecordKind.Statement, InCongress,
            index < 3 ? ["housing", "health"] : ["housing"])).ToArray();

        var column = TalkAndActionProfile.Of(119, entries, []).Statements;

        Assert.Equal(5, column.WithIssue);
        Assert.Equal(1.0, column.ShareOf("housing"));
        Assert.Equal(0.6, column.ShareOf("health"));
    }

    [Fact]
    public void ARecordWithNoTrackedIssueIsCountedApartAndStaysOutOfTheShareDenominator()
    {
        var entries = Enumerable.Range(0, 5).Select(index => Entry(index, PublishedRecordKind.Statement, InCongress, ["housing"]))
            .Concat([Entry(7, PublishedRecordKind.Statement, InCongress, []), Entry(8, PublishedRecordKind.Statement, InCongress, [])]);
        var votes = new[] { Vote(1, PublishedVoteKind.Passage, []), Vote(2, PublishedVoteKind.Passage, ["housing"]) };

        var profile = TalkAndActionProfile.Of(119, entries, votes);

        Assert.Equal((5, 2), (profile.Statements.WithIssue, profile.Statements.WithoutIssue));
        Assert.Equal(1.0, profile.Statements.ShareOf("housing"));
        Assert.Equal((1, 1), (profile.Votes.WithIssue, profile.Votes.WithoutIssue));
    }

    [Theory]
    [InlineData(4, false)]
    [InlineData(5, true)]
    public void ASharePassesTheThresholdOnlyWithEnoughRecordsThatHaveAnIssueWhileTheCountIsAlwaysThere(int records, bool showsShare)
    {
        var entries = Enumerable.Range(0, records).Select(index => Entry(index, PublishedRecordKind.Statement, InCongress, ["housing"]));

        var column = TalkAndActionProfile.Of(119, entries, []).Statements;

        Assert.Equal(records, column.CountOf("housing"));
        Assert.Equal(showsShare, column.ShareOf("housing") is not null);
    }

    [Fact]
    public void RecordsWithNoIssueDoNotLiftAColumnOverTheThreshold()
    {
        var entries = Enumerable.Range(0, 10).Select(index => Entry(index, PublishedRecordKind.Statement, InCongress, index < 4 ? ["housing"] : []));

        Assert.Null(TalkAndActionProfile.Of(119, entries, []).Statements.ShareOf("housing"));
    }

    [Fact]
    public void TheIssuesOfThePanelAreTheSortedUnionOfTheIssuesOfAllThreeColumns()
    {
        var profile = TalkAndActionProfile.Of(119, [Entry(1, PublishedRecordKind.Statement, InCongress, ["tax", "health"]),
            Entry(2, PublishedRecordKind.Action, InCongress, ["housing"])], [Vote(1, PublishedVoteKind.Passage, ["health", "defense"])]);

        Assert.Equal(["defense", "health", "housing", "tax"], profile.IssueIds.ToArray());
    }

    private static PublishedRecordEntry Entry(int id, PublishedRecordKind kind, DateOnly day, string[] issueIds) => new()
    {
        Officials = [],
        PolicyAreaIds = [],
        Kind = kind,
        RecordId = id.ToString("x64"),
        RevisionNumber = 1,
        Headline = $"Entry {id}",
        FirstPublishedAtUtc = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero),
        EventDate = day,
        IssueIds = issueIds
    };

    private static PublishedOfficialVote Vote(int id, PublishedVoteKind kind, string[] issueIds) => new()
    {
        RecordId = id.ToString("x64"),
        VotedOn = InCongress,
        Chamber = PublishedChamber.House,
        Congress = 119,
        Session = 1,
        RollCallNumber = id + 1,
        Cast = "Yea",
        Result = "Passed",
        Totals = new() { Yea = 218, Nay = 210, Present = 1, NotVoting = 5 },
        Party = "Republican",
        Headline = $"Vote {id}",
        Kind = kind,
        IssueIds = issueIds
    };
}
