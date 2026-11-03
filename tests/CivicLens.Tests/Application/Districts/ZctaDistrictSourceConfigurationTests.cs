using System.Text.Json;
using CivicLens.Application.Collection;
using CivicLens.Application.Collection.Jobs;
using CivicLens.Application.Configuration;
using CivicLens.Collection.Contracts;
using CivicLens.Tests.Fixtures;

namespace CivicLens.Tests.Application.Districts;

public sealed class ZctaDistrictSourceConfigurationTests
{
    private static readonly DateOnly Today = new(2026, 11, 3);

    [Fact]
    public void AWeeklyCheckValidatesAndASourceCoversNoPeopleSoItStaysEligible()
    {
        var configuration = DistrictFixtures.Configuration();

        configuration.Validate();

        Assert.Equal(604800, DistrictFixtures.Source().ResolveCheckIntervalSeconds());
        Assert.True(configuration.IsActiveOn(DistrictFixtures.Source(), Today));
    }

    [Fact]
    public void TheShippedSourceIsOffByDefaultAndChecksOnceAWeek()
    {
        var shipped = JsonSerializer.Deserialize<CollectionConfiguration>(
            File.ReadAllText(Path.Combine(RepositoryRoot.FullName, "config", "federal-coverage.json")), ConfigurationJson.Options)!;
        var source = shipped.Sources.Single(candidate => candidate.ApiParser == ApiParser.CensusZctaDistricts);

        shipped.Validate();

        Assert.False(source.Enabled);
        Assert.Equal(604800, source.ResolveCheckIntervalSeconds());
    }

    [Fact]
    public void ACredentialOrAValidatorIsRejectedBecauseTheFileIsPublicAndDownloadedInFull()
    {
        var configuration = DistrictFixtures.Configuration();
        var source = DistrictFixtures.Source();

        Assert.Throws<ArgumentException>((configuration with { Sources = [source with { Credential = CollectionCredentials.CongressGov }] }).Validate);
        Assert.Throws<ArgumentException>((configuration with { Sources = [source with { ETag = "\"v1\"" }] }).Validate);
    }

    [Fact]
    public void ASecondDistrictSourceIsRejectedBecauseEachKindHasOneSource()
    {
        var second = DistrictFixtures.Source() with { Id = "second", Url = DistrictFixtures.SourceUrl + "?copy=1" };

        var error = Assert.Throws<ArgumentException>((DistrictFixtures.Configuration() with { Sources = [DistrictFixtures.Source(), second] }).Validate);

        Assert.Contains("district source", error.Message);
    }

    [Fact]
    public void ADistrictJobIsBoundToTheSourceUrlAndTheDistrictResource()
    {
        var configured = new ConfiguredCollectionSource(DistrictFixtures.Configuration(), DistrictFixtures.SourceId, Today);

        _ = configured.CreateApiJobDefinition(ApiResource.ZctaDistricts, DistrictFixtures.SourceUrl, Today, CollectionJobPriority.Check);
        Assert.Throws<ArgumentException>(() =>
            configured.CreateApiJobDefinition(ApiResource.MemberRoster, DistrictFixtures.SourceUrl, Today, CollectionJobPriority.Check));
        Assert.Throws<ArgumentException>(() =>
            configured.CreateApiJobDefinition(ApiResource.ZctaDistricts, DistrictFixtures.SourceUrl + "?x=1", Today, CollectionJobPriority.Check));
    }
}
