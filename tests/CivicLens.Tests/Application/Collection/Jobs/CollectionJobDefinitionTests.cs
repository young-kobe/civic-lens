using CivicLens.Application.Collection;
using CivicLens.Application.Collection.Jobs;

namespace CivicLens.Tests.Application.Collection.Jobs;

public sealed class CollectionJobDefinitionTests
{
    [Fact]
    public void DefinitionSnapshotsOnlySelectedSourceAndResolvesAggregateDefaults()
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
