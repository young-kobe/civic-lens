using CivicLens.Application.Roster;
using CivicLens.Tests.Fixtures;

namespace CivicLens.Tests.Application.Roster;

public sealed class ProposeRosterChangesTests
{
    [Fact]
    public async Task AProposedIdIsTheNameInLowerCaseWordsAndGetsTheBioguideIdWhenAnotherPersonHoldsIt()
    {
        var configuration = RosterFixtures.Configuration(RosterFixtures.Person("maria-cantwell", "Another Maria Cantwell", "Z000001"));

        var proposal = await new ProposeRosterChanges(new FixedMemberRoster(RosterFixtures.Members()), new FixedPersonRegistry()).ExecuteAsync(configuration, default);

        Assert.Equal(["maria-cantwell-c000127", "eleanor-holmes-norton", "aisha-wahab"], proposal!.Joins.Select(join => join.Id));
    }

    [Fact]
    public async Task AMemberWhoHasAPersonRowKeepsTheMintedIdEvenWhenTheConfigurationNoLongerHoldsTheOldIdOfTheSameName()
    {
        // The old John Smith (S1) left and the configuration dropped him, but his people row stays and holds "john-smith".
        // The roster job minted "john-smith-s2" for the new John Smith (S2). Propose must name the minted ID.
        var configuration = RosterFixtures.Configuration();
        var member = RosterFixtures.Members()[0] with { FullName = "John Smith", BioguideId = "S000002" };
        var people = new FixedPersonRegistry(keys: [new("john-smith", "S000001"), new("john-smith-s000002", "S000002")]);

        var proposal = await new ProposeRosterChanges(new FixedMemberRoster([member]), people).ExecuteAsync(configuration, default);

        Assert.Equal("john-smith-s000002", Assert.Single(proposal!.Joins).Id);
    }

    [Fact]
    public async Task AnIdOfAFormerPersonOutsideTheConfigurationIsTakenSoProposeAndMintingGiveTheSameId()
    {
        // A former John Smith (S1) holds "john-smith" in people only. The new member has no row yet.
        // AdmitAsync sees every people row, so it would mint "john-smith-s000003". Propose must name the same ID.
        var member = RosterFixtures.Members()[0] with { FullName = "John Smith", BioguideId = "S000003" };
        var people = new FixedPersonRegistry(keys: [new("john-smith", "S000001")]);

        var proposal = await new ProposeRosterChanges(new FixedMemberRoster([member]), people).ExecuteAsync(RosterFixtures.Configuration(), default);

        Assert.Equal("john-smith-s000003", Assert.Single(proposal!.Joins).Id);
    }

    [Fact]
    public async Task ANameWithNoLettersFallsBackToTheLowercaseBioguideIdSoTheIdIsNeverEmpty()
    {
        var configuration = RosterFixtures.Configuration();
        var member = RosterFixtures.Members()[0] with { FullName = "...", BioguideId = "Q000001" };

        var proposal = await new ProposeRosterChanges(new FixedMemberRoster([member]), new FixedPersonRegistry()).ExecuteAsync(configuration, default);

        Assert.Equal("q000001", Assert.Single(proposal!.Joins).Id);
    }

    [Fact]
    public async Task AConfiguredPersonWithOfficesIsNotProposedAsALeaveBecauseTheConfigurationKeepsThemOutsideTheRoster()
    {
        var kept = RosterFixtures.Person("jd-vance", "J.D. Vance", "V000137") with { Offices = [new() { Title = "Vice President", StartsOn = new(2025, 1, 20) }] };
        var gone = RosterFixtures.Person("old-member", "Old Member", "O000001");

        var proposal = await new ProposeRosterChanges(new FixedMemberRoster(RosterFixtures.Members()), new FixedPersonRegistry()).ExecuteAsync(RosterFixtures.Configuration(kept, gone), default);

        Assert.Equal(["old-member"], proposal!.Leaves.Select(leave => leave.Id));
    }

    [Fact]
    public async Task NoSnapshotYieldsNoProposalSoAnEmptyRosterNeverReadsAsEveryoneLeaving()
    {
        var configuration = RosterFixtures.Configuration(RosterFixtures.Person("a", "A", "A000001"));

        Assert.Null(await new ProposeRosterChanges(new FixedMemberRoster([]), new FixedPersonRegistry()).ExecuteAsync(configuration, default));
    }
}
