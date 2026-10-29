using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CivicLens.Application.Collection;
using CivicLens.Collection.Contracts;

namespace CivicLens.Tests.Application.Collection;

public sealed class CollectionConfigurationRevisionTests
{
    [Fact]
    public void CreatedRevisionCapturesConfigurationAndChangesIdentityWithContent()
    {
        var configuration = Configuration(["person"]);

        var revision = CollectionConfigurationRevision.Create(configuration);
        configuration.Sources[0].Coverage![0] = new SourceCoverageConfiguration { PersonId = "changed" };

        Assert.Contains("person", revision.Json, StringComparison.Ordinal);
        Assert.DoesNotContain("changed", revision.Json, StringComparison.Ordinal);
        Assert.NotEqual(revision.Id, CollectionConfigurationRevision.Create(Configuration(["person"]) with
        { People = [new PersonConfiguration { Id = "person", Name = "Corrected" }] }).Id);
        Assert.Matches("^[0-9a-f]{64}$", revision.Id);
    }

    [Fact]
    public void RevisionRoundTripsThroughJsonWithValueEquality()
    {
        var revision = CollectionConfigurationRevision.Create(Configuration(["person"]));
        var serialized = JsonSerializer.Serialize(revision, CollectionProtocol.JsonOptions);

        var deserialized = JsonSerializer.Deserialize<CollectionConfigurationRevision>(serialized, CollectionProtocol.JsonOptions);

        Assert.Equal(revision, deserialized);
    }

    [Fact]
    public void TheRevisionParsesOnceAndReadConfigurationReturnsDetachedCopies()
    {
        var revision = CollectionConfigurationRevision.Create(Configuration(["person"]));
        var read = revision.ReadConfiguration();
        read.Sources[0].Coverage![0] = new SourceCoverageConfiguration { PersonId = "changed" };

        Assert.Same(revision.Configuration, revision.Configuration);
        Assert.NotSame(revision.Configuration, revision.ReadConfiguration());
        Assert.Equal("person", revision.Configuration.Sources[0].Coverage![0].PersonId);
        Assert.Equal("person", revision.ReadConfiguration().Sources[0].Coverage![0].PersonId);
    }

    [Fact]
    public void ConstructorRejectsHashMismatchMalformedJsonAndInvalidConfiguration()
    {
        var revision = CollectionConfigurationRevision.Create(Configuration(["person"]));
        Assert.Throws<ArgumentException>(() => new CollectionConfigurationRevision(new string('0', 64), revision.Json));

        var malformedJson = "{";
        Assert.Throws<ArgumentException>(() => new CollectionConfigurationRevision(Hash(malformedJson), malformedJson));

        const string invalidJson = "{\"version\":1,\"people\":[{\"id\":\"person\",\"name\":\"Official\"}],\"sources\":[{\"id\":\"source\",\"coverage\":[{\"personId\":\"person\"}],\"url\":\"relative\",\"allowedOrigin\":\"https://example.test\",\"allowedPathPrefix\":\"/\"}]}";
        Assert.Throws<ArgumentException>(() => new CollectionConfigurationRevision(Hash(invalidJson), invalidJson));
    }

    [Fact]
    public void RevisionWithoutBioguideIdRetainsItsOriginalJsonAndId()
    {
        const string json = "{\"version\":1,\"people\":[{\"id\":\"person\",\"name\":\"Official\"}],\"sources\":[{\"id\":\"source\",\"coverage\":[{\"personId\":\"person\"}],\"url\":\"https://example.test/page\",\"allowedOrigin\":\"https://example.test\",\"allowedPathPrefix\":\"/\",\"mode\":\"page\",\"maxCandidates\":100,\"enabled\":true,\"maxRequests\":5,\"maxBytes\":2000000,\"timeoutSeconds\":30,\"minDelayMilliseconds\":1000}]}";
        const string id = "47d4eda0147b7865512e403224dd257444fbe472eea347d7dea6c555772e41e3";

        var revision = new CollectionConfigurationRevision(id, json);

        Assert.Equal(json, revision.Json);
        Assert.DoesNotContain("bioguideId", revision.Json, StringComparison.Ordinal);
        Assert.DoesNotContain("website", revision.Json, StringComparison.Ordinal);
        Assert.Equal(id, CollectionConfigurationRevision.Create(revision.ReadConfiguration()).Id);
    }

    [Fact]
    public void BioguideIdSurvivesRevisionRoundTrip()
    {
        var configuration = new CollectionConfiguration
        {
            Version = 1,
            People = [new PersonConfiguration { Id = "person", Name = "Official", BioguideId = "W000817" }],
            Sources = [Configuration(["person"]).Sources[0]]
        };

        var revision = CollectionConfigurationRevision.Create(configuration);

        Assert.Contains("\"bioguideId\":\"W000817\"", revision.Json, StringComparison.Ordinal);
        Assert.Equal("W000817", revision.ReadConfiguration().People[0].BioguideId);
    }

    [Fact]
    public void WikidataIdSurvivesRevisionRoundTrip()
    {
        var configuration = new CollectionConfiguration
        {
            Version = 1,
            People = [new PersonConfiguration { Id = "person", Name = "Official", WikidataId = "Q22686" }],
            Sources = [Configuration(["person"]).Sources[0]]
        };

        var revision = CollectionConfigurationRevision.Create(configuration);

        Assert.Contains("\"wikidataId\":\"Q22686\"", revision.Json, StringComparison.Ordinal);
        Assert.Equal("Q22686", revision.ReadConfiguration().People[0].WikidataId);
    }

    [Fact]
    public void WebsiteSurvivesRevisionRoundTrip()
    {
        var configuration = new CollectionConfiguration
        {
            Version = 1,
            People = [new PersonConfiguration { Id = "person", Name = "Official", Website = "https://www.whitehouse.gov" }],
            Sources = [Configuration(["person"]).Sources[0]]
        };

        var revision = CollectionConfigurationRevision.Create(configuration);

        Assert.Contains("\"website\":\"https://www.whitehouse.gov\"", revision.Json, StringComparison.Ordinal);
        Assert.Equal("https://www.whitehouse.gov", revision.ReadConfiguration().People[0].Website);
    }

    [Fact]
    public void WithholdPhotoSurvivesRevisionRoundTripAndAnAbsentFlagLeavesTheSnapshotUnchanged()
    {
        var configuration = Configuration(["person"]);
        var withheld = configuration with { People = [new PersonConfiguration { Id = "person", Name = "Official", WithholdPhoto = true }] };

        var revision = CollectionConfigurationRevision.Create(withheld);

        Assert.Contains("\"withholdPhoto\":true", revision.Json, StringComparison.Ordinal);
        Assert.True(revision.ReadConfiguration().People[0].WithholdPhoto);
        Assert.DoesNotContain("withholdPhoto", CollectionConfigurationRevision.Create(configuration).Json, StringComparison.Ordinal);
    }

    [Fact]
    public void OfficesSurviveRevisionRoundTripAndAbsentOfficesLeaveTheSnapshotUnchanged()
    {
        var configuration = Configuration(["person"]);
        var withOffice = configuration with
        {
            People = [new PersonConfiguration { Id = "person", Name = "Official", Offices = [new() { Title = "Secretary", StartsOn = new(2025, 1, 21), Kind = ConfiguredOfficeKind.Judicial }] }]
        };

        var revision = CollectionConfigurationRevision.Create(withOffice);

        Assert.Contains("\"offices\":[{\"title\":\"Secretary\",\"startsOn\":\"2025-01-21\",\"kind\":\"judicial\"}]", revision.Json, StringComparison.Ordinal);
        Assert.Equal("Secretary", revision.ReadConfiguration().People[0].Offices![0].Title);
        Assert.Equal(ConfiguredOfficeKind.Judicial, revision.ReadConfiguration().People[0].Offices![0].Kind);
        Assert.DoesNotContain("offices", CollectionConfigurationRevision.Create(configuration).Json, StringComparison.Ordinal);
    }

    [Fact]
    public void ExplicitSourceIntervalChangesConfigurationRevision()
    {
        var configuration = Configuration(["person"]);
        var defaultRevision = CollectionConfigurationRevision.Create(configuration);
        var configuredRevision = CollectionConfigurationRevision.Create(configuration with
        {
            Sources = [configuration.Sources[0] with { CheckIntervalSeconds = 1800 }]
        });

        Assert.NotEqual(defaultRevision.Id, configuredRevision.Id);
        Assert.Contains("\"checkIntervalSeconds\":1800", configuredRevision.Json, StringComparison.Ordinal);
        Assert.Equal(1800, configuredRevision.ReadConfiguration().Sources[0].ResolveCheckIntervalSeconds());
    }

    private static CollectionConfiguration Configuration(string[] personIds) => new()
    {
        People = [new PersonConfiguration { Id = "person", Name = "Official" }],
        Sources =
        [
            new WatchedSourceConfiguration
            {
                Id = "source",
                Coverage = [.. personIds.Select(personId => new SourceCoverageConfiguration { PersonId = personId })],
                Url = "https://example.test/page",
                AllowedOrigin = "https://example.test",
                AllowedPathPrefix = "/"
            }
        ]
    };

    private static string Hash(string json) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
}
