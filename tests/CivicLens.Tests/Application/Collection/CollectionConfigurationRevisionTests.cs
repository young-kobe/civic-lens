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
        var personIds = new[] { "person" };
        var configuration = Configuration(personIds);

        var revision = CollectionConfigurationRevision.Create(configuration);
        personIds[0] = "changed";

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
    public void ReadConfigurationReturnsDetachedConfiguration()
    {
        var revision = CollectionConfigurationRevision.Create(Configuration(["person"]));
        var read = revision.ReadConfiguration();
        read.Sources[0].PersonIds![0] = "changed";

        Assert.Equal("person", revision.ReadConfiguration().Sources[0].PersonIds![0]);
    }

    [Fact]
    public void ConstructorRejectsHashMismatchMalformedJsonAndInvalidConfiguration()
    {
        var revision = CollectionConfigurationRevision.Create(Configuration(["person"]));
        Assert.Throws<ArgumentException>(() => new CollectionConfigurationRevision(new string('0', 64), revision.Json));

        var malformedJson = "{";
        Assert.Throws<ArgumentException>(() => new CollectionConfigurationRevision(Hash(malformedJson), malformedJson));

        const string invalidJson = "{\"version\":1,\"people\":[{\"id\":\"person\",\"name\":\"Official\"}],\"sources\":[{\"id\":\"source\",\"personIds\":[\"person\"],\"url\":\"relative\",\"allowedOrigin\":\"https://example.test\",\"allowedPathPrefix\":\"/\"}]}";
        Assert.Throws<ArgumentException>(() => new CollectionConfigurationRevision(Hash(invalidJson), invalidJson));
    }

    [Fact]
    public void LegacyCanonicalConfigurationRevisionRetainsItsOriginalJsonAndId()
    {
        const string json = "{\"version\":1,\"people\":[{\"id\":\"person\",\"name\":\"Official\"}],\"sources\":[{\"id\":\"source\",\"personIds\":[\"person\"],\"url\":\"https://example.test/page\",\"allowedOrigin\":\"https://example.test\",\"allowedPathPrefix\":\"/\",\"mode\":\"page\",\"maxCandidates\":100,\"enabled\":true,\"maxRequests\":5,\"maxBytes\":2000000,\"timeoutSeconds\":30,\"minDelayMilliseconds\":1000}]}";
        const string id = "7f4521051fe8c22d4588fdf8e61e4bffdf76d6130e472472b2d4595e117ded16";

        var revision = new CollectionConfigurationRevision(id, json);

        Assert.Equal(json, revision.Json);
        Assert.DoesNotContain("checkIntervalSeconds", revision.Json, StringComparison.Ordinal);
        Assert.Equal(id, CollectionConfigurationRevision.Create(revision.ReadConfiguration()).Id);
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
                PersonIds = personIds,
                Url = "https://example.test/page",
                AllowedOrigin = "https://example.test",
                AllowedPathPrefix = "/"
            }
        ]
    };

    private static string Hash(string json) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
}
