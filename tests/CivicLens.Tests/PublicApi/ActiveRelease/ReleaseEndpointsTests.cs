using System.Net;
using System.Net.Http.Json;
using CivicLens.PublicApi.Search;
using CivicLens.Publication.Contracts;
using CivicLens.Tests.Infrastructure.Collection;
using CivicLens.Tests.PublicApi.Votes;

namespace CivicLens.Tests.PublicApi.ActiveRelease;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class ReleaseEndpointsTests(PostgresCollection postgres)
{
    private static readonly SeededVote Vote = new('e', "2026-03-03");

    [Fact]
    public async Task TheReleaseEndpointAnswersFromTheViewsAndNotFromTheSearchIndexBecauseTheTwoCanDifferAfterAPublish()
    {
        var site = new FakeReleaseSite();
        site.Publish(1, PublishedSamples.Statement('a', "Rent cap"));
        await using var host = await ApiHost.StartAsync(postgres, site);
        await host.WaitForReleaseAsync();
        await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 7, new DateTimeOffset(2026, 6, 9, 12, 30, 0, TimeSpan.Zero));
        await ViewsSeed.AddVotesAsync(host.OperationalDatabase, Vote with { FirstRelease = 7 });

        var release = await host.Client.GetFromJsonAsync<ReleaseInfo>("/v1/release", PublicationProtocol.JsonOptions);

        Assert.Equal(new ReleaseInfo(7, new DateTimeOffset(2026, 6, 9, 12, 30, 0, TimeSpan.Zero)), release);
    }

    [Fact]
    public async Task TheReleaseEndpointFollowsARollbackAtOnce()
    {
        await using var host = await ApiHost.StartAsync(postgres, new FakeReleaseSite());
        await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 1, new DateTimeOffset(2026, 6, 2, 0, 0, 0, TimeSpan.Zero));
        await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 2, new DateTimeOffset(2026, 6, 9, 0, 0, 0, TimeSpan.Zero));
        await ViewsSeed.AddVotesAsync(host.OperationalDatabase, Vote);
        var before = await host.Client.GetFromJsonAsync<ReleaseInfo>("/v1/release", PublicationProtocol.JsonOptions);

        await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 1, new DateTimeOffset(2026, 6, 2, 0, 0, 0, TimeSpan.Zero));
        var after = await host.Client.GetFromJsonAsync<ReleaseInfo>("/v1/release", PublicationProtocol.JsonOptions);

        Assert.Equal(2, before!.ReleaseNumber);
        Assert.Equal(new ReleaseInfo(1, new DateTimeOffset(2026, 6, 2, 0, 0, 0, TimeSpan.Zero)), after);
    }

    [Fact]
    public async Task WithNoActiveReleaseTheReleaseEndpointAnswers503()
    {
        await using var host = await ApiHost.StartAsync(postgres, new FakeReleaseSite());

        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await host.Client.GetAsync("/v1/release")).StatusCode);
    }
}
