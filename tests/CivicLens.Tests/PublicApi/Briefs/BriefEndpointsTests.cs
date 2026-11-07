using System.Net;
using System.Text.Json;
using CivicLens.Publication.Contracts;
using CivicLens.Tests.Fixtures;
using CivicLens.Tests.Infrastructure.Collection;
using CivicLens.Tests.PublicApi.Votes;

namespace CivicLens.Tests.PublicApi.Briefs;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class BriefEndpointsTests(PostgresCollection postgres)
{
    [Theory]
    [InlineData("not-a-record")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public async Task InvalidAndMissingBriefsAnswerNotFound(string recordId)
    {
        await using var host = await ApiHost.StartAsync(postgres, new FakeReleaseSite());
        using var response = await host.Client.GetAsync("/v1/briefs/" + recordId);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task EndpointReturnsOrderedTextWithPinnedTargetHeadlinesAndRollbackRestoresAWithdrawnBrief()
    {
        await using var host = await ApiHost.StartAsync(postgres, new FakeReleaseSite());
        await PublishedBriefContentSeed.SeedAsync(host.OperationalDatabase);

        using var response = await host.Client.GetAsync("/v1/briefs/" + BriefFixtures.RecordId);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = body.RootElement;
        Assert.Equal(["release", "brief", "paragraphs", "citations"], root.EnumerateObject().Select(property => property.Name));
        Assert.Equal(1, root.GetProperty("release").GetInt32());
        var brief = root.GetProperty("brief");
        Assert.Equal(BriefFixtures.RecordId, brief.GetProperty("recordId").GetString());
        Assert.Equal(1, brief.GetProperty("revisionNumber").GetInt32());
        Assert.Equal("2026-10-05", brief.GetProperty("weekStart").GetString());
        Assert.Equal("Published brief headline", brief.GetProperty("headline").GetString());
        Assert.Equal("model", brief.GetProperty("textOrigin").GetString());
        var paragraphs = root.GetProperty("paragraphs").EnumerateArray().ToArray();
        Assert.Equal(["Text paragraph one", "Text paragraph two"], paragraphs.Select(paragraph => paragraph.GetProperty("text").GetString()));
        Assert.Equal(["R1"], paragraphs[0].GetProperty("citationIds").EnumerateArray().Select(id => id.GetString()));
        Assert.Equal(["R2", "R3"], paragraphs[1].GetProperty("citationIds").EnumerateArray().Select(id => id.GetString()));
        var citations = root.GetProperty("citations").EnumerateArray().ToArray();
        Assert.Equal(["R1", "R2", "R3"], citations.Select(citation => citation.GetProperty("id").GetString()));
        Assert.Equal(["statement", "documentChange", "action"], citations.Select(citation => citation.GetProperty("kind").GetString()));
        Assert.Equal(["Pinned statement headline", "Pinned document headline", "Bill 7"], citations.Select(citation => citation.GetProperty("headline").GetString()));
        Assert.All(citations, citation => Assert.Equal(1, citation.GetProperty("revisionNumber").GetInt32()));
        Assert.DoesNotContain("Suggestion", body.RootElement.GetRawText(), StringComparison.Ordinal);

        var seeder = new BriefWorldSeeder(host.OperationalDatabase);
        await seeder.ReleaseWithoutApprovalsAsync(2, BriefFixtures.InWeek.AddDays(1), active: false);
        await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 2, BriefFixtures.InWeek.AddDays(1));
        using var withdrawn = await host.Client.GetAsync("/v1/briefs/" + BriefFixtures.RecordId);
        Assert.Equal(HttpStatusCode.NotFound, withdrawn.StatusCode);
        await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 1, BriefFixtures.InWeek);
        using var restored = await host.Client.GetAsync("/v1/briefs/" + BriefFixtures.RecordId);
        Assert.Equal(HttpStatusCode.OK, restored.StatusCode);
    }


}
