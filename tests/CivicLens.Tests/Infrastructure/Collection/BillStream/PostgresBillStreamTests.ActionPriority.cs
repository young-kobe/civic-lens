using CivicLens.Application.Collection.Bills;
using CivicLens.Core.Relationships;
using CivicLens.Tests.Fixtures;
using Npgsql;
using static CivicLens.Tests.Fixtures.RelationshipFixtures;

namespace CivicLens.Tests.Infrastructure.Collection.BillStream;

public sealed partial class PostgresBillStreamTests
{
    private const string BillSeven = "H.R. 7";

    [Theory]
    [InlineData(true, 2)]
    [InlineData(false, 3)]
    public async Task ABillThatNoStatementNamesIsTierTwoWhenItHasATrackedIssueAndTierThreeWhenNot(bool hasTrackedIssue, int expected)
    {
        var bill = await StartPassedBillAsync(cosponsors: 0, summaries: 0, hasTrackedIssue);

        await AdmitActionsAsync(bill.Actions, new CongressBillActionsPage([], null));

        Assert.Equal(expected, await ActionPriorityAsync());
    }

    [Fact]
    public async Task ABillThatADraftedStatementNamesIsTierOneBeforeAnyOfficialIsChecked()
    {
        await StatementNamingBillSevenAsync("markey");
        var bill = await StartPassedBillAsync(cosponsors: 0, summaries: 0);

        await AdmitActionsAsync(bill.Actions, new CongressBillActionsPage([], null));

        Assert.Equal(1, await ActionPriorityAsync());
    }

    [Fact]
    public async Task ANewStatementCandidateRaisesThePendingActionRowThatItsBillNumberNamesAndNoOther()
    {
        var bill = await StartPassedBillAsync(cosponsors: 0, summaries: 0);
        await AdmitActionsAsync(bill.Actions, new CongressBillActionsPage([], null));
        Assert.Equal(3, await ActionPriorityAsync());

        _ = await seeder.SeedAsync("Said: I back H.R. 8 today.", "https://example.test/pages/other");
        Assert.Equal(3, await ActionPriorityAsync());

        _ = await seeder.SeedAsync("Said: I back " + BillSeven + " today.", "https://example.test/pages/seven");
        Assert.Equal(1, await ActionPriorityAsync());
    }

    [Fact]
    public async Task ARunningRowKeepsItsPriorityWhenANewStatementNamesItsBill()
    {
        var bill = await StartPassedBillAsync(cosponsors: 0, summaries: 0);
        await AdmitActionsAsync(bill.Actions, new CongressBillActionsPage([], null));
        Assert.NotNull(await ActionStore().TryClaimAsync(Assert.Single(await CandidatesAsync()).CandidateId, Lease, default));

        _ = await seeder.SeedAsync("Said: I back " + BillSeven + " today.", "https://example.test/pages/seven");

        Assert.Equal(3, await ActionPriorityAsync());
    }

    [Fact]
    public async Task TheExactTierLowersAnEarlyTierOneWhenNoStatementOfASponsorPairs()
    {
        await StatementNamingBillSevenAsync("markey");
        var candidate = await CandidateWithoutSummariesAsync();
        Assert.Equal(1, await ActionPriorityAsync());

        await SettleAsync(candidate, Revision(candidate, 1));

        Assert.Equal(3, await ActionPriorityAsync());
    }

    [Fact]
    public async Task TheExactTierCountsAPairWithAStatementThatNoPersonHasApprovedYet()
    {
        var candidate = await CandidateWithoutSummariesAsync();
        Assert.Equal(3, await ActionPriorityAsync());
        await StatementNamingBillSevenAsync("warren");

        await SettleAsync(candidate, Revision(candidate, 1));

        Assert.Equal(1, await ActionPriorityAsync());
    }

    private Task StatementNamingBillSevenAsync(string speaker)
    {
        var statement = StatementAboutBill(speaker, 119, 7);
        return new RelationshipWorldSeeder(connectionString).StatementAsync(
            statement with { Revision = statement.Revision with { BillNumbers = [BillSeven] } }, "AwaitingReview");
    }

    private async Task<int> ActionPriorityAsync()
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT priority FROM action_analysis", connection);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }
}
