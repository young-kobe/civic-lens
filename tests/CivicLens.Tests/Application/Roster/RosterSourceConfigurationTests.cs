using CivicLens.Application.Collection;
using CivicLens.Application.Collection.Jobs;
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
    public void AValidatorIsRejectedBecauseA304WouldStopTheDailyCheck()
    {
        var configuration = RosterFixtures.Configuration();
        var source = RosterFixtures.Source();

        Assert.Throws<ArgumentException>((configuration with { Sources = [source with { ETag = "\"v1\"" }] }).Validate);
        Assert.Throws<ArgumentException>((configuration with { Sources = [source with { LastModified = DateTimeOffset.UnixEpoch }] }).Validate);
    }

    [Fact]
    public void ASecondRosterSourceIsRejectedBecauseProposalsReadOneSource()
    {
        var second = RosterFixtures.Source() with { Id = "second", Url = RosterFixtures.SourceUrl + "?copy=1" };
        var configuration = RosterFixtures.Configuration() with { Sources = [RosterFixtures.Source(), second] };

        Assert.Throws<ArgumentException>(configuration.Validate);
    }

    [Fact]
    public void ASecondSourceOfTheOtherRosterKindIsAccepted()
    {
        var configuration = RosterFixtures.Configuration() with { Sources = [RosterFixtures.Source(), ExecutiveFixtures.Source()] };

        configuration.Validate();
    }

    [Fact]
    public void ASecondExecutiveSourceIsRejectedBecauseEachKindHasOneSource()
    {
        var second = ExecutiveFixtures.Source() with { Id = "second", Url = ExecutiveFixtures.SourceUrl + "?copy=1" };
        var configuration = RosterFixtures.Configuration() with { Sources = [ExecutiveFixtures.Source(), second] };

        Assert.Throws<ArgumentException>(configuration.Validate);
    }

    [Fact]
    public void AnExecutiveSourceFollowsTheRulesOfARosterFileAndStaysEligible()
    {
        var configuration = ExecutiveFixtures.Configuration();
        var source = configuration.Sources.Single();

        configuration.Validate();
        Assert.Throws<ArgumentException>((configuration with
        {
            Sources = [source with { Credential = CollectionCredentials.CongressGov }]
        }).Validate);
        Assert.Throws<ArgumentException>((configuration with { Sources = [source with { ETag = "\"v1\"" }] }).Validate);
        Assert.Throws<ArgumentException>((configuration with { Sources = [source with { BackfillFrom = Today }] }).Validate);
        Assert.Empty(configuration.CoveredPersonIdsOf(source, Today));
        Assert.Equal(source, configuration.EligibleSource(source.Id, Today));
    }

    [Fact]
    public void AnExecutiveJobIsBoundToTheSourceUrlAndTheExecutiveResource()
    {
        var source = new ConfiguredCollectionSource(ExecutiveFixtures.Configuration(), ExecutiveFixtures.SourceId, Today);

        _ = source.CreateApiJobDefinition(ApiResource.ExecutiveRoster, ExecutiveFixtures.SourceUrl, Today, CollectionJobPriority.Check);
        Assert.Throws<ArgumentException>(() =>
            source.CreateApiJobDefinition(ApiResource.MemberRoster, ExecutiveFixtures.SourceUrl, Today, CollectionJobPriority.Check));
        Assert.Throws<ArgumentException>(() =>
            source.CreateApiJobDefinition(ApiResource.ExecutiveRoster, ExecutiveFixtures.SourceUrl + "?x=1", Today, CollectionJobPriority.Check));
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

        _ = source.CreateApiJobDefinition(ApiResource.MemberRoster, RosterFixtures.SourceUrl, Today, CollectionJobPriority.Check);
        Assert.Throws<ArgumentException>(() =>
            source.CreateApiJobDefinition(ApiResource.MemberRoster, RosterFixtures.SourceUrl + "?x=1", Today, CollectionJobPriority.Check));
    }
}
