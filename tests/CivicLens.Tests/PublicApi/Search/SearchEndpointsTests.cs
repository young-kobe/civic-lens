using System.Net;
using CivicLens.PublicApi;
using System.Net.Http.Json;
using System.Text.Json;
using CivicLens.PublicApi.Search;
using CivicLens.Publication.Contracts;
using CivicLens.Tests.Infrastructure.Collection;
using Npgsql;

namespace CivicLens.Tests.PublicApi.Search;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class SearchEndpointsTests(PostgresCollection postgres)
{
    private async Task<ApiHost> StartWithReleasesAsync(Func<PublicApiOptions, PublicApiOptions>? change = null)
    {
        var site = new FakeReleaseSite();
        site.Publish(1, PublishedSamples.Statement('a', "Rent cap", official: "o1", issues: ["housing"], host: "one.gov", date: new DateOnly(2026, 3, 1)));
        var host = await ApiHost.StartAsync(postgres, site, change);
        await host.WaitForReleaseAsync();
        return host;
    }

    [Fact]
    public async Task TheReleaseEndpointNamesTheActiveReleaseAndItsPublicationTime()
    {
        await using var host = await StartWithReleasesAsync();

        var release = await host.Client.GetFromJsonAsync<ReleaseInfo>("/v1/release", PublicationProtocol.JsonOptions);

        Assert.Equal(new ReleaseInfo(1, new DateTimeOffset(2026, 6, 2, 0, 0, 0, TimeSpan.Zero)), release);
    }

    [Fact]
    public async Task SearchReturnsTheManifestEntryFieldsTheSnippetAndTheRecordPaths()
    {
        await using var host = await StartWithReleasesAsync();

        using var json = JsonDocument.Parse(await host.Client.GetStringAsync("/v1/search?q=rent"));

        var hit = json.RootElement.GetProperty("hits")[0];
        Assert.Equal(1, json.RootElement.GetProperty("release").GetInt32());
        Assert.Equal("statement", hit.GetProperty("kind").GetString());
        Assert.Equal("Rent cap", hit.GetProperty("headline").GetString());
        Assert.Equal("o1", hit.GetProperty("officials")[0].GetProperty("id").GetString());
        Assert.Equal("housing", hit.GetProperty("issueIds")[0].GetString());
        Assert.Equal("one.gov", hit.GetProperty("sourceHost").GetString());
        Assert.Equal("2026-03-01", hit.GetProperty("eventDate").GetString());
        Assert.Equal($"records/{new string('a', 64)}.html", hit.GetProperty("pagePath").GetString());
        Assert.Equal($"records/{new string('a', 64)}.json", hit.GetProperty("dataPath").GetString());
        Assert.False(string.IsNullOrEmpty(hit.GetProperty("snippet").GetString()));
    }

    [Fact]
    public async Task EveryResponseAllowsAnyOriginAndSetsNoCookie()
    {
        await using var host = await StartWithReleasesAsync();

        using var response = await host.Client.GetAsync("/v1/search?q=rent");

        Assert.Equal("*", Assert.Single(response.Headers.GetValues("Access-Control-Allow-Origin")));
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task APinnedReleaseThatWasNeverIngestedAnswers404WithTheReasonReleaseNotIndexed()
    {
        await using var host = await StartWithReleasesAsync();

        using var response = await host.Client.GetAsync("/v1/search?q=rent&release=9");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("release not indexed", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NotFound, (await host.Client.GetAsync($"/v1/records/{new string('a', 64)}?release=9")).StatusCode);
    }

    [Fact]
    public async Task BeforeAnyReleaseIsIndexedTheServiceAnswers503SoACallerKnowsItIsNotReadyYet()
    {
        await using var host = await ApiHost.StartAsync(postgres, new FakeReleaseSite());

        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await host.Client.GetAsync("/v1/release")).StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await host.Client.GetAsync("/v1/search?q=rent")).StatusCode);
    }

    [Theory]
    [InlineData("limit=0")]
    [InlineData("limit=51")]
    [InlineData("limit=x")]
    [InlineData("kind=nonsense")]
    [InlineData("from=2026-13-01")]
    [InlineData("release=0")]
    [InlineData("cursor=nonsense")]
    [InlineData("q=a&q=b")]
    public async Task BadParametersAreRefusedWith400(string query)
    {
        await using var host = await StartWithReleasesAsync();

        Assert.Equal(HttpStatusCode.BadRequest, (await host.Client.GetAsync("/v1/search?" + query)).StatusCode);
    }

    [Fact]
    public async Task AQueryOfMoreThanTheMaximumLengthIsRefusedAndOneAtTheMaximumIsAccepted()
    {
        await using var host = await StartWithReleasesAsync();

        Assert.Equal(HttpStatusCode.OK, (await host.Client.GetAsync("/v1/search?q=" + new string('a', SearchRequest.MaximumQueryLength))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await host.Client.GetAsync("/v1/search?q=" + new string('a', SearchRequest.MaximumQueryLength + 1))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await host.Client.GetAsync("/v1/search?official=" + new string('a', PublishedRecordEntry.MaximumFilterValueLength + 1))).StatusCode);
    }

    [Fact]
    public async Task TheLimitIsHonoredUpToTheMaximumAndTheCursorContinuesInTheSameRelease()
    {
        var site = new FakeReleaseSite();
        site.Publish(1, Enumerable.Range(0, 3).Select(i => PublishedSamples.Statement((char)('a' + i), "Rent " + i)).ToArray());
        await using var host = await ApiHost.StartAsync(postgres, site, options => options with { PollInterval = TimeSpan.FromMilliseconds(100) });
        await host.WaitForReleaseAsync();

        using var first = JsonDocument.Parse(await host.Client.GetStringAsync("/v1/search?q=rent&limit=2"));
        var cursor = first.RootElement.GetProperty("nextCursor").GetString();
        site.Publish(2, PublishedSamples.Statement('f', "Rent new"));
        await host.WaitForReleaseAsync(2);
        using var second = JsonDocument.Parse(await host.Client.GetStringAsync("/v1/search?q=rent&limit=2&cursor=" + cursor));

        Assert.Equal(2, first.RootElement.GetProperty("hits").GetArrayLength());
        Assert.Equal(1, second.RootElement.GetProperty("hits").GetArrayLength());
        Assert.Equal(1, second.RootElement.GetProperty("release").GetInt32());
        Assert.Equal(JsonValueKind.Null, second.RootElement.GetProperty("nextCursor").ValueKind);
    }

    [Fact]
    public async Task WhenTheSiteGoesBackToAnEarlierReleaseTheReleaseEndpointAndAnUnpinnedSearchFollowAndAPinToTheLaterReleaseStillWorks()
    {
        var five = PublishedSamples.Statement('a', "Rent five");
        var site = new FakeReleaseSite();
        site.Publish(5, five);
        await using var host = await ApiHost.StartAsync(postgres, site, options => options with { PollInterval = TimeSpan.FromMilliseconds(100) });
        await host.WaitForReleaseAsync(5);
        site.Publish(6, PublishedSamples.Statement('b', "Rent six"));
        await host.WaitForReleaseAsync(6);

        site.Publish(5, five);
        await host.WaitForReleaseAsync(5);

        using var active = JsonDocument.Parse(await host.Client.GetStringAsync("/v1/search?q=rent"));
        using var pinned = JsonDocument.Parse(await host.Client.GetStringAsync("/v1/search?q=rent&release=6"));
        Assert.Equal("Rent five", Assert.Single(active.RootElement.GetProperty("hits").EnumerateArray()).GetProperty("headline").GetString());
        Assert.Equal(5, active.RootElement.GetProperty("release").GetInt32());
        Assert.Equal("Rent six", Assert.Single(pinned.RootElement.GetProperty("hits").EnumerateArray()).GetProperty("headline").GetString());
    }

    [Fact]
    public async Task ARecordLookupReturnsTheIndexRowAndAnUnknownRecordAnswers404()
    {
        await using var host = await StartWithReleasesAsync();

        var found = await host.Client.GetFromJsonAsync<JsonElement>($"/v1/records/{new string('a', 64)}", PublicationProtocol.JsonOptions);

        Assert.Equal("Rent cap", found.GetProperty("headline").GetString());
        Assert.Equal(1, found.GetProperty("firstRelease").GetInt32());
        Assert.False(found.TryGetProperty("snippet", out _));
        Assert.Equal(HttpStatusCode.NotFound, (await host.Client.GetAsync($"/v1/records/{new string('b', 64)}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await host.Client.GetAsync("/v1/records/" + new string('b', 65))).StatusCode);
    }

    [Fact]
    public async Task AStatementThatRunsLongerThanTheTimeoutIsStoppedAndAnswers503()
    {
        await using var host = await StartWithReleasesAsync(options => options with { QueryTimeout = TimeSpan.FromMilliseconds(300) });
        await using var blocker = new NpgsqlConnection(host.Options.Database);
        await blocker.OpenAsync();
        await using var transaction = await blocker.BeginTransactionAsync();
        await using (var command = new NpgsqlCommand("LOCK TABLE records IN ACCESS EXCLUSIVE MODE", blocker, transaction))
            await command.ExecuteNonQueryAsync();

        using var response = await host.Client.GetAsync("/v1/search?q=rent");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }
}
