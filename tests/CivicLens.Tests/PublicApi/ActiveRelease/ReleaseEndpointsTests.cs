using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
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
        await using var host = await ApiHost.StartAsync(postgres, publish: site => site.PublishAsync(1, PublishedSamples.Statement('a', "Rent cap")));
        await host.WaitForReleaseAsync();
        await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 7, new DateTimeOffset(2026, 6, 9, 12, 30, 0, TimeSpan.Zero));
        await ViewsSeed.AddVotesAsync(host.OperationalDatabase, Vote with { FirstRelease = 7 });

        var release = await host.Client.GetFromJsonAsync<ReleaseInfo>("/v1/release", PublicationProtocol.JsonOptions);

        Assert.Equal(new ReleaseInfo(7, new DateTimeOffset(2026, 6, 9, 12, 30, 0, TimeSpan.Zero)), release);
    }

    [Fact]
    public async Task AnActiveReleaseWithNoRecordsStillHasAnIdentity()
    {
        await using var host = await ApiHost.StartAsync(postgres);
        await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 2, new DateTimeOffset(2026, 6, 9, 12, 30, 0, TimeSpan.Zero), recordCount: 0);

        var release = await host.Client.GetFromJsonAsync<ReleaseInfo>("/v1/release", PublicationProtocol.JsonOptions);

        Assert.Equal(new ReleaseInfo(2, new DateTimeOffset(2026, 6, 9, 12, 30, 0, TimeSpan.Zero)), release);
    }

    [Fact]
    public async Task EmptyRecordListsKeepTheActiveReleaseAndTrueAbsenceStillReturnsNotFound()
    {
        await using var host = await ApiHost.StartAsync(postgres);
        await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 2, new DateTimeOffset(2026, 6, 9, 12, 30, 0, TimeSpan.Zero), recordCount: 0);

        foreach (var (path, collection) in new[]
        {
            ("/v1/votes", "votes"),
            ("/v1/bills", "bills"),
            ("/v1/statements", "statements"),
            ("/v1/officials", "officials"),
            ("/v1/home", "votes")
        })
        {
            using var response = await host.Client.GetAsync(path);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(2, body.RootElement.GetProperty("release").GetInt32());
            Assert.Empty(body.RootElement.GetProperty(collection).EnumerateArray());
        }

        using var missingBrief = await host.Client.GetAsync("/v1/briefs/" + new string('a', 64));
        Assert.Equal(HttpStatusCode.NotFound, missingBrief.StatusCode);
    }

    [Fact]
    public async Task TheReleaseEndpointFollowsARollbackAtOnce()
    {
        await using var host = await ApiHost.StartAsync(postgres);
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
        await using var host = await ApiHost.StartAsync(postgres);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await host.Client.GetAsync("/v1/release")).StatusCode);
    }
}
