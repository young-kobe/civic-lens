using System.Text.Json;
using CivicLens.Collection.Contracts;
using CivicLens.Application.Collection;
using CivicLens.Application.Collection.Jobs;

namespace CivicLens.Tests.Application.Collection.Jobs;

public sealed class CollectionJobDefinitionTests
{
    [Fact]
    public void DefinitionSnapshotsExecutionSettingsAndConfigurationRevision()
    {
        var config = Config();
        var definition = CollectionJobDefinition.FromConfiguration(config, "first");
        config.Sources[0] = config.Sources[0] with { Url = "https://changed.test/" };

        Assert.Equal("https://example.test/page", definition.Url);
        Assert.Equal(15, definition.Policy.ResolveMaxTotalRequests(definition.CreateRequest("job", "captures")));
        Assert.Equal(6_000_000, definition.Policy.ResolveMaxTotalBytes(definition.CreateRequest("job", "captures")));
        Assert.Equal(90, definition.Policy.ResolveMaxTotalTimeoutSeconds(definition.CreateRequest("job", "captures")));
    }

    [Fact]
    public void AggregateBudgetCannotBeSmallerThanOneAttempt()
    {
        var definition = CollectionJobDefinition.FromConfiguration(Config(), "first") with
        {
            Policy = new CollectionJobPolicy { MaxTotalRequests = 4 }
        };

        Assert.Throws<ArgumentException>(definition.Validate);
    }

    [Fact]
    public void CoverageAndRevisionRemainBoundAfterConfigMutationAndJsonRoundTrip()
    {
        var date = new DateOnly(2026, 7, 1);
        var config = DatedConfig(date);
        var definition = CollectionJobDefinition.FromConfiguration(config, "first", date);
        var json = JsonSerializer.Serialize(definition, CollectionProtocol.JsonOptions);
        var restored = JsonSerializer.Deserialize<CollectionJobDefinition>(json, CollectionProtocol.JsonOptions)!;
        config.Sources[0].Coverage![0] = config.Sources[0].Coverage![0] with { EndsBefore = date };
        config.People[0] = config.People[0] with { Name = "Changed" };

        restored.Validate();
        Assert.Equal(definition, restored);
        Assert.Equal(date, restored.CoverageAsOf);
        Assert.Equal(["person"], restored.ConfigurationRevision!.ReadConfiguration().GetCoveredPersonIds("first", date));
        Assert.Equal("Person", restored.ConfigurationRevision.ReadConfiguration().People[0].Name);
        Assert.Throws<ArgumentException>(() => CollectionJobDefinition.FromConfiguration(config, "first", date));
    }

    [Fact]
    public void BindingRejectsMissingContextChangedSettingsAndInactiveCoverage()
    {
        var date = new DateOnly(2026, 7, 1);
        var definition = CollectionJobDefinition.FromConfiguration(DatedConfig(date), "first", date);
        Assert.Throws<ArgumentException>(() => (definition with { CoverageAsOf = null }).Validate());
        Assert.Throws<ArgumentException>(() => (definition with { ConfigurationRevision = null }).Validate());
        Assert.Throws<ArgumentException>(() => (definition with { CoverageAsOf = date.AddDays(-1) }).Validate());
        Assert.Throws<ArgumentException>(() => (definition with { MaxBytes = 100 }).Validate());
        Assert.Throws<ArgumentException>(() => (definition with { Url = "https://example.test/page/other" }).Validate());
    }

    [Fact]
    public void DiscoveryChildrenKeepRevisionButMayChangeUrlAndClearValidators()
    {
        var config = Config();
        config.Sources[0] = config.Sources[0] with { Mode = CollectionMode.Html, ETag = "\"etag\"" };
        var definition = CollectionJobDefinition.FromConfiguration(config, "first", new DateOnly(2026, 7, 1));
        var child = definition with { Mode = CollectionMode.Page, Url = "https://example.test/page/article", ETag = null };
        child.Validate();
        Assert.Equal(definition.ConfigurationRevision, child.ConfigurationRevision);
        Assert.Throws<ArgumentException>(() => (child with { Url = "https://other.test/page" }).Validate());
        Assert.Throws<ArgumentException>(() => (child with { ETag = "\"etag\"" }).Validate());
    }

    [Fact]
    public void LegacyReplayMatchesExecutionSettingsWithoutInventingProvenance()
    {
        var config = Config();
        var definition = CollectionJobDefinition.FromConfiguration(config, "first", new DateOnly(2026, 7, 1));
        var legacy = definition with { ConfigurationRevision = null, CoverageAsOf = null };
        var legacyJson = JsonSerializer.Serialize(legacy, CollectionProtocol.JsonOptions);
        Assert.DoesNotContain("configurationRevision", legacyJson);
        Assert.DoesNotContain("coverageAsOf", legacyJson);
        Assert.True(definition.MatchesReplayOf(legacy));
        Assert.False((definition with { MaxRequests = 4 }).MatchesReplayOf(legacy));
        Assert.False(legacy.MatchesReplayOf(definition));

        config.People[0] = config.People[0] with { Name = "Corrected" };
        var corrected = CollectionJobDefinition.FromConfiguration(config, "first", definition.CoverageAsOf);
        Assert.False(corrected.MatchesReplayOf(definition));
        Assert.False((definition with { CoverageAsOf = new DateOnly(2026, 7, 2) }).MatchesReplayOf(definition));
    }

    private static CollectionConfiguration DatedConfig(DateOnly startsOn)
    {
        var config = Config();
        return config with
        {
            Version = 2,
            Sources = [config.Sources[0] with
            {
                PersonIds = null,
                Coverage = [new SourceCoverageConfiguration { PersonId = "person", StartsOn = startsOn }]
            }]
        };
    }

    private static CollectionConfiguration Config() => new()
    {
        People = [new PersonConfiguration { Id = "person", Name = "Person" }],
        Sources =
        [
            new WatchedSourceConfiguration
            {
                Id = "first", PersonIds = ["person"], Url = "https://example.test/page",
                AllowedOrigin = "https://example.test", AllowedPathPrefix = "/page"
            },
            new WatchedSourceConfiguration
            {
                Id = "other", PersonIds = ["person"], Url = "https://other.test/page",
                AllowedOrigin = "https://other.test", AllowedPathPrefix = "/page"
            }
        ]
    };
}
