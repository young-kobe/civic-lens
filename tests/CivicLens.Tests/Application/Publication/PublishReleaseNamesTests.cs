using System.Text.Json;
using CivicLens.Application.Issues;
using CivicLens.Application.Publication;
using CivicLens.Publication.Contracts;

namespace CivicLens.Tests.Application.Publication;

public sealed class PublishReleaseNamesTests
{
    [Fact]
    public async Task TheNamesOfTheCommitAreTheOfficialsAndTheCatalogOfTheReleaseFilesBecauseTheApiMustShowWhatTheReleaseShowed()
    {
        var scenario = ScenarioWithDefinedIssue();
        var summary = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner,
            PublicationScenario.Request("k", new PublishedRecordReference(PublishedRecordKind.Statement, scenario.AddStatement('a'))));

        var names = scenario.Publications.LastCommit!.Names;
        var officials = Read<PublishedOfficials>(scenario, summary.DirectoryName, PublicationProtocol.OfficialsPath);
        var catalog = Read<PublishedCatalog>(scenario, summary.DirectoryName, PublicationProtocol.CatalogPath);
        Assert.Equal(JsonSerializer.Serialize(officials.Officials, PublicationProtocol.JsonOptions),
            JsonSerializer.Serialize(names.Officials, PublicationProtocol.JsonOptions));
        Assert.Equal(catalog.Issues.Select(entry => (entry.Id, entry.Name)), names.Facets.Issues.Select(issue => (issue.Id, issue.Name)));
        Assert.Equal(catalog.PolicyAreas.Select(entry => (entry.Id, entry.Name)), names.Facets.PolicyAreas.Select(area => (area.Id, area.Name)));
    }

    [Fact]
    public async Task AnIssueNameCarriesTheDefinitionIncludesAndExcludesOfTheConfigurationAndAPolicyAreaCarriesNone()
    {
        var scenario = ScenarioWithDefinedIssue();

        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner,
            PublicationScenario.Request("k", new PublishedRecordReference(PublishedRecordKind.Statement, scenario.AddStatement('a'))));

        var names = scenario.Publications.LastCommit!.Names;
        var definition = names.Facets.Issues.Single(issue => issue.Id == "housing").Definition!;
        Assert.Equal("Rent and home prices.", definition.Text);
        Assert.Equal(["Rent caps", "Eviction rules"], [.. definition.Includes]);
        Assert.Equal(["Zoning votes"], [.. definition.Excludes]);
        Assert.Null(names.Facets.PolicyAreas.Single().Definition);
    }

    private static PublicationScenario ScenarioWithDefinedIssue()
    {
        var scenario = new PublicationScenario
        {
            Issues = new IssueConfiguration
            {
                PolicyAreas = [new() { Id = "land-use", Name = "Land use" }],
                Issues =
                [
                    new()
                    {
                        Id = "housing", Name = "Housing costs", Definition = "Rent and home prices.", Includes = ["Rent caps", "Eviction rules"],
                        Excludes = ["Zoning votes"], PolicyAreaIds = ["land-use"]
                    }
                ]
            }
        };
        scenario.SeedRelease(withFiles: true, [new PublishedRecordEntry
        {
            Kind = PublishedRecordKind.DocumentChange,
            Officials = [],
            RecordId = 0.ToString("x32"),
            RevisionNumber = 1,
            Headline = "Seeded",
            FirstPublishedAtUtc = PublicationScenario.ApprovedAt.AddMinutes(-10),
            IssueIds = ["housing"],
            PolicyAreaIds = ["land-use"]
        }]);
        return scenario;
    }

    private static T Read<T>(PublicationScenario scenario, string directory, string path) =>
        JsonSerializer.Deserialize<T>(scenario.Releases.Directories[directory][path], PublicationProtocol.JsonOptions)!;
}
