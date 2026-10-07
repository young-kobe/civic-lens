using CivicLens.Application.Collection;
using CivicLens.Application.Collection.Jobs;
using CivicLens.Collection.Contracts;

namespace CivicLens.Tests.Application.Collection;

public sealed class ConfiguredCollectionSourceTests
{
    private static readonly DateOnly AdmittedOn = new(2020, 1, 1);
    private static readonly DateOnly ExpiredOn = new(2021, 1, 1);

    [Fact]
    public void ReplayReturnsTheSavedDecisionWhileNewAdmissionRequiresEligibility()
    {
        var config = Configuration();
        var source = new ConfiguredCollectionSource(config, "source");
        var saved = source.CreateJobDefinition(AdmittedOn);

        Assert.Same(saved, source.CreateJobDefinition(ExpiredOn, saved));
        Assert.Equal(AdmittedOn, saved.CoverageAsOf);
        Assert.Throws<ArgumentException>(() => source.CreateJobDefinition(ExpiredOn));
        Assert.Throws<ArgumentException>(() => new ConfiguredCollectionSource(config, "source", AdmittedOn.AddDays(1))
            .CreateJobDefinition(ExpiredOn, saved));
        Assert.Same(saved, new ConfiguredCollectionSource(config, "source", AdmittedOn).CreateJobDefinition(ExpiredOn, saved));
    }

    [Fact]
    public void ConfigurationChangesRemainConflictsAndCannotRewriteTheSavedDecision()
    {
        var config = Configuration();
        var source = new ConfiguredCollectionSource(config, "source");
        var saved = source.CreateJobDefinition(AdmittedOn);
        config.People[0] = config.People[0] with { Name = "Corrected" };

        Assert.Same(saved, source.CreateJobDefinition(ExpiredOn, saved));
        Assert.Throws<ArgumentException>(() => new ConfiguredCollectionSource(config, "source")
            .CreateJobDefinition(ExpiredOn, saved));
        config.Sources[0] = config.Sources[0] with { Enabled = false };
        Assert.Throws<ArgumentException>(() => new ConfiguredCollectionSource(config, "source")
            .CreateJobDefinition(AdmittedOn));
    }

    [Theory]
    [InlineData(CollectionMode.Feed)]
    [InlineData(CollectionMode.Html)]
    public void DiscoveryReplayRetainsTheTemplateAndRejectsChangedDates(CollectionMode mode)
    {
        var config = Configuration(mode);
        var source = new ConfiguredCollectionSource(config, "source");
        var saved = source.CreateDiscoveryAdmission("attempt", "key", AdmittedOn);
        var replay = source.CreateDiscoveryAdmission("attempt", "key", ExpiredOn, saved.ArticleTemplate);

        Assert.Equal(saved, replay);
        Assert.Same(saved.ArticleTemplate, replay.ArticleTemplate);
        Assert.Throws<ArgumentException>(() => source.CreateDiscoveryAdmission("attempt", "new", ExpiredOn));
        Assert.Throws<ArgumentException>(() => new ConfiguredCollectionSource(config, "source", AdmittedOn.AddDays(1))
            .CreateDiscoveryAdmission("attempt", "key", ExpiredOn, saved.ArticleTemplate));
        Assert.Throws<ArgumentException>(() => source.CreateJobDefinition(ExpiredOn, saved.ArticleTemplate));
    }

    [Fact]
    public void LegacyReplayDoesNotAcquireARevisionOrDependOnCurrentCoverage()
    {
        var config = Configuration();
        var saved = CollectionJobDefinition.FromConfiguration(config, "source", AdmittedOn) with
        { ConfigurationRevision = null, CoverageAsOf = null };
        config.Sources[0] = config.Sources[0] with { Enabled = false };
        var source = new ConfiguredCollectionSource(config, "source");

        Assert.Same(saved, source.CreateJobDefinition(ExpiredOn, saved));
        Assert.Null(saved.ConfigurationRevision);
        Assert.Null(saved.CoverageAsOf);
        Assert.Same(saved, new ConfiguredCollectionSource(config, "source", ExpiredOn)
            .CreateJobDefinition(ExpiredOn, saved));
        Assert.Throws<ArgumentException>(() => source.CreateJobDefinition(ExpiredOn));
        config.Sources[0] = config.Sources[0] with { MaxBytes = 100 };
        Assert.Throws<ArgumentException>(() => new ConfiguredCollectionSource(config, "source")
            .CreateJobDefinition(ExpiredOn, saved));
    }

    private static CollectionConfiguration Configuration(CollectionMode mode = CollectionMode.Page) => new()
    {
        Version = 2,
        People = [new PersonConfiguration { Id = "person", Name = "Person" }],
        Sources = [new WatchedSourceConfiguration
        {
            Id = "source", Url = "https://example.test/page", AllowedOrigin = "https://example.test",
            AllowedPathPrefix = "/", Mode = mode,
            Coverage = [new SourceCoverageConfiguration { PersonId = "person", StartsOn = AdmittedOn, EndsBefore = ExpiredOn }]
        }]
    };
}
