using CivicLens.Application.Collection;
using CivicLens.Collection.Contracts;
using CivicLens.Tests.Fixtures;

namespace CivicLens.Tests.Application.Roster;

public sealed class RosterSourceConfigurationTests
{
    private static readonly DateOnly Today = new(2026, 10, 19);

    [Fact]
    public void ARosterSourceValidatesWithoutACredentialOrCoverageList()
    {
        RosterFixtures.Configuration(RosterFixtures.Person("a", "A", "A000001")).Validate();
    }

    [Fact]
    public void ACredentialIsRejectedBecauseTheFileIsPublic()
    {
        var configuration = RosterFixtures.Configuration();
        var source = RosterFixtures.Source() with { Credential = CollectionCredentials.CongressGov };

        Assert.Throws<ArgumentException>((configuration with { Sources = [source] }).Validate);
    }

    [Fact]
    public void ACoverageListOrCongressOptionIsRejectedBecauseTheRosterCoversNoPeople()
    {
        var configuration = RosterFixtures.Configuration(RosterFixtures.Person("a", "A", "A000001"));
        var source = RosterFixtures.Source();

        Assert.Throws<ArgumentException>((configuration with
        {
            Sources = [source with { Coverage = [new SourceCoverageConfiguration { PersonId = "a" }] }]
        }).Validate);
        Assert.Throws<ArgumentException>((configuration with { Sources = [source with { MinimumCongress = 119 }] }).Validate);
        Assert.Throws<ArgumentException>((configuration with { Sources = [source with { BackfillFrom = Today }] }).Validate);
    }

    [Fact]
    public void ASecondRosterSourceIsRejectedBecauseProposalsReadOneSource()
    {
        var second = RosterFixtures.Source() with { Id = "second", Url = RosterFixtures.SourceUrl + "?copy=1" };
        var configuration = RosterFixtures.Configuration() with { Sources = [RosterFixtures.Source(), second] };

        Assert.Throws<ArgumentException>(configuration.Validate);
    }

    [Fact]
    public void ARosterSourceCoversNoPeopleYetStaysEligibleSoTheDailyCheckRuns()
    {
        var configuration = RosterFixtures.Configuration(RosterFixtures.Person("a", "A", "A000001"));
        var source = configuration.Sources.Single();

        Assert.Empty(configuration.CoveredPersonIdsOf(source, Today));
        Assert.Equal(source, configuration.EligibleSource(source.Id, Today));
    }

    [Fact]
    public void ARosterJobIsBoundToTheSourceUrlAndNoOtherUrl()
    {
        var source = new ConfiguredCollectionSource(RosterFixtures.Configuration(), RosterFixtures.SourceId, Today);

        _ = source.CreateApiJobDefinition(ApiResource.MemberRoster, RosterFixtures.SourceUrl, Today);
        Assert.Throws<ArgumentException>(() =>
            source.CreateApiJobDefinition(ApiResource.MemberRoster, RosterFixtures.SourceUrl + "?x=1", Today));
    }
}
