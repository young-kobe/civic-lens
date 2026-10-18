using CivicLens.Tests.Fixtures;
using static CivicLens.Tests.Fixtures.RelationshipFixtures;

namespace CivicLens.Tests.Infrastructure.Collection.HouseVotes;

public sealed partial class PostgresHouseVoteTests
{
    private const string BillFourteen = "H.Con.Res. 14";

    [Theory]
    [InlineData(true, 2)]
    [InlineData(false, 3)]
    public async Task AVoteWaitingForItsBillIsQueuedAtTheTierOfTheBillDetail(bool hasTrackedIssue, int expected)
    {
        var vote = HouseVoteFixtures.Vote(100, Updated);
        var (_, members) = await DetailWithMembersJobAsync(vote);
        await AdmitMembersAsync(members, vote);

        await AdmitBillDetailAsync(hasTrackedIssue: hasTrackedIssue);

        Assert.Equal(expected, await VotePriorityAsync());
    }

    [Fact]
    public async Task AVoteWhoseBillIsAlreadyKnownTakesTheTrackedIssueOfTheSavedBillGate()
    {
        await AdmitBillDetailAsync(hasTrackedIssue: true);
        var vote = HouseVoteFixtures.Vote(100, Updated);
        var (_, members) = await DetailWithMembersJobAsync(vote);

        await AdmitMembersAsync(members, vote);

        Assert.Equal(2, await VotePriorityAsync());
    }

    [Fact]
    public async Task AVoteWithoutABillIsTierThreeBecauseNoStatementCanNameItsBill()
    {
        var vote = HouseVoteFixtures.Vote(100, Updated, billType: null, billNumber: null);
        var (_, members) = await DetailWithMembersJobAsync(vote);

        await AdmitMembersAsync(members, vote);

        Assert.Equal(3, await VotePriorityAsync());
    }

    [Fact]
    public async Task AVoteOnABillThatADraftedStatementNamesIsTierOne()
    {
        await StatementNamingBillFourteenAsync("warren");

        _ = await PendingCandidateAsync();

        Assert.Equal(1, await VotePriorityAsync());
    }

    [Fact]
    public async Task TheExactTierLowersAnEarlyTierOneWhenNoMemberOfTheVoteIsTheSpeaker()
    {
        await StatementNamingBillFourteenAsync("warren");
        var candidate = await PendingCandidateAsync();
        Assert.Equal(1, await VotePriorityAsync());

        await SettleAsync(candidate, await RevisionAsync(candidate, 1));

        Assert.Equal(3, await VotePriorityAsync());
    }

    [Fact]
    public async Task TheExactTierKeepsTierOneWhenAMemberOfTheVoteIsTheSpeakerOfAnUnapprovedStatement()
    {
        var candidate = await PendingCandidateAsync();
        await StatementNamingBillFourteenAsync("with-id-a");

        await SettleAsync(candidate, await RevisionAsync(candidate, 1));

        Assert.Equal(1, await VotePriorityAsync());
    }

    private Task StatementNamingBillFourteenAsync(string speaker)
    {
        var statement = StatementAboutBill(speaker, 119, 14, "H.Con.Res.");
        return new RelationshipWorldSeeder(connectionString).StatementAsync(
            statement with { Revision = statement.Revision with { BillNumbers = [BillFourteen] } }, "AwaitingReview");
    }

    private async Task<int> VotePriorityAsync() => Convert.ToInt32(await ScalarAsync("SELECT priority FROM vote_analysis"));
}
