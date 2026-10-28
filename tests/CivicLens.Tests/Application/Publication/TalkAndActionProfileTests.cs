using CivicLens.Application.Publication;
using CivicLens.Publication.Contracts;

namespace CivicLens.Tests.Application.Publication;

public sealed class TalkAndActionProfileTests
{
    private static readonly DateOnly InCongress = new(2026, 1, 1);

    [Fact]
    public void OnlySubstantiveVoteKindsCountSoAProceduralVoteNeverRaisesTheVotesTheyCast()
    {
        PublishedVoteKind[] substantive =
            [PublishedVoteKind.Passage, PublishedVoteKind.Suspension, PublishedVoteKind.Amendment, PublishedVoteKind.ConferenceReport,
                PublishedVoteKind.VetoOverride, PublishedVoteKind.Nomination];
        var procedural = Enum.GetValues<PublishedVoteKind>().Except(substantive).ToArray();
        var votes = Enumerable.Concat(substantive, procedural).Select((kind, index) => Vote(index, kind, ["housing"]));

        var profile = TalkAndActionProfile.Of(119, [], votes);

        Assert.Equal(substantive.Length, profile.Votes.CountOf("housing"));
        Assert.Equal(substantive.Order(), Enum.GetValues<PublishedVoteKind>().Where(kind => kind.IsSubstantive()).Order());
    }

    [Fact]
    public void ANotVotingCastLeavesTheVotesColumnWhilePresentAndACandidateNameStayIn()
    {
        var votes = new[] { Vote(1, PublishedVoteKind.Passage, ["housing"]), Vote(2, PublishedVoteKind.Passage, ["housing"]) with { Cast = PublishedOfficialVote.NotVoting },
            Vote(3, PublishedVoteKind.Passage, ["housing"]) with { Cast = "Present" }, Vote(4, PublishedVoteKind.Cloture, ["housing"]) with { Cast = PublishedOfficialVote.NotVoting },
            Vote(5, PublishedVoteKind.Passage, ["housing"]) with { Cast = "Pelosi" } };

        var profile = TalkAndActionProfile.Of(119, [], votes);

        Assert.Equal(3, profile.Votes.CountOf("housing"));
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

        Assert.Equal((1, 1, 0), (profile.Statements.CountOf("housing"), profile.SponsoredOn("housing").Length, profile.Votes.CountOf("housing")));
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

        Assert.Equal((2, 0), (profile.Statements.CountOf("housing"), profile.SponsoredOn("housing").Length));
    }

    [Fact]
    public void AnEntryWithNoEventDateCountsInTheCongressOfItsFirstPublicationDay()
    {
        var entry = Entry(1, PublishedRecordKind.Statement, InCongress, ["housing"]) with
        {
            EventDate = null,
            FirstPublishedAtUtc = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero)
        };

        Assert.Equal(1, TalkAndActionProfile.Of(119, [entry], []).Statements.CountOf("housing"));
        Assert.Equal(0, TalkAndActionProfile.Of(118, [entry], []).Statements.CountOf("housing"));
    }

    [Fact]
    public void ARecordWithSeveralIssuesCountsOncePerIssueAndARecordWithNoIssueCountsOnNoIssue()
    {
        var entries = new[] { Entry(1, PublishedRecordKind.Statement, InCongress, ["housing", "health"]), Entry(2, PublishedRecordKind.Statement, InCongress, ["housing"]),
            Entry(3, PublishedRecordKind.Statement, InCongress, []) };

        var column = TalkAndActionProfile.Of(119, entries, []).Statements;

        Assert.Equal((2, 1, 0), (column.CountOf("housing"), column.CountOf("health"), column.CountOf("tax")));
    }

    [Fact]
    public void ASponsoredBillIsListedOnEachOfItsIssuesWithItsBillTitleInTheCurrentCongressOnly()
    {
        var bill = Entry(1, PublishedRecordKind.Action, InCongress, ["housing", "health"]) with { BillTitle = "Home Act" };
        var older = Entry(2, PublishedRecordKind.Action, new DateOnly(2023, 5, 1), ["housing"]) with { BillTitle = "Old Act" };

        var profile = TalkAndActionProfile.Of(119, [bill, older], []);

        Assert.Equal(["Home Act"], profile.SponsoredOn("housing").Select(entry => entry.BillTitle));
        Assert.Equal(["Home Act"], profile.SponsoredOn("health").Select(entry => entry.BillTitle));
        Assert.Empty(profile.SponsoredOn("tax"));
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
        SplitVote = false,
        Party = "Republican",
        Headline = $"Vote {id}",
        Kind = kind,
        IssueIds = issueIds
    };
}
