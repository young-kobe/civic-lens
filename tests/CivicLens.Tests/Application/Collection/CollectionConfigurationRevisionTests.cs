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
