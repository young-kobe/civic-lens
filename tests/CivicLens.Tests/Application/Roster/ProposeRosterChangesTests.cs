using CivicLens.Application.Roster;
using CivicLens.Tests.Fixtures;

namespace CivicLens.Tests.Application.Roster;

public sealed class ProposeRosterChangesTests
{
    [Fact]
    public async Task AProposedIdIsTheNameInLowerCaseWordsAndGetsTheBioguideIdWhenAnotherPersonHoldsIt()
    {
        var configuration = RosterFixtures.Configuration(RosterFixtures.Person("maria-cantwell", "Another Maria Cantwell", "Z000001"));

        var proposal = await new ProposeRosterChanges(new FixedMemberRoster(RosterFixtures.Members())).ExecuteAsync(configuration, default);

        Assert.Equal(["maria-cantwell-c000127", "eleanor-holmes-norton", "aisha-wahab"], proposal!.Joins.Select(join => join.Id));
    }

    [Fact]
    public async Task ANameWithNoLettersFallsBackToTheLowercaseBioguideIdSoTheIdIsNeverEmpty()
    {
        var configuration = RosterFixtures.Configuration();
        var member = RosterFixtures.Members()[0] with { FullName = "...", BioguideId = "Q000001" };

        var proposal = await new ProposeRosterChanges(new FixedMemberRoster([member])).ExecuteAsync(configuration, default);

        Assert.Equal("q000001", Assert.Single(proposal!.Joins).Id);
    }

    [Fact]
    public async Task NoSnapshotYieldsNoProposalSoAnEmptyRosterNeverReadsAsEveryoneLeaving()
    {
        var configuration = RosterFixtures.Configuration(RosterFixtures.Person("a", "A", "A000001"));

        Assert.Null(await new ProposeRosterChanges(new FixedMemberRoster([])).ExecuteAsync(configuration, default));
    }
}
