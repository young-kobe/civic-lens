using CivicLens.Application.Documents;
using CivicLens.Application.Collection;

namespace CivicLens.Tests.Application.Collection;

public sealed class DocumentProfileConfigurationTests
{
    [Fact]
    public void VersionTwoProfilesAreReusableAndSourcesResolveTheirAssignments()
    {
        var configuration = Configuration() with
        {
            DocumentProfiles =
            [
                new DocumentProfileConfiguration { Id = "article", Selector = "main", ExcludedSelectors = [".nav"] },
                new DocumentProfileConfiguration { Id = "press", Selector = "#content" }
            ],
            Sources =
            [
                Source("first", "article"),
                Source("second", "article")
            ]
        };

        configuration.Validate();
        var profile = configuration.DocumentProfiles[0].ToProfile();
        Assert.Equal("article", profile.Id);
        Assert.Equal("main", profile.Selector);
        Assert.Equal(new[] { ".nav" }, profile.ExcludedSelectors);
    }

    [Fact]
    public void RejectsDuplicateProfilesMissingReferencesAndMalformedSelectors()
    {
        Assert.Throws<ArgumentException>(() => (Configuration() with
        {
            DocumentProfiles = [new() { Id = "same", Selector = "main" }, new() { Id = "same", Selector = ".body" }]
        }).Validate());

        Assert.Throws<ArgumentException>(() => (Configuration() with
        {
            Sources = [Source("source", "unknown")]
        }).Validate());

        Assert.Throws<ArgumentException>(() => (Configuration() with
        {
            DocumentProfiles = [new() { Id = "profile", Selector = "main > article" }]
        }).Validate());
    }

    [Fact]
    public void DocumentProfilesAndAssignmentsRequireConfigurationVersionTwo()
    {
        var v1WithProfiles = Configuration() with
        {
            Version = 1,
            Sources = [Source("source", null) with { PersonIds = ["person"], Coverage = null }],
            DocumentProfiles = [new() { Id = "profile", Selector = "main" }]
        };
        Assert.Throws<ArgumentException>(v1WithProfiles.Validate);

        var v1WithAssignment = Configuration() with
        {
            Version = 1,
            Sources = [Source("source", "profile") with { PersonIds = ["person"], Coverage = null }]
        };
        Assert.Throws<ArgumentException>(v1WithAssignment.Validate);
    }

    private static CollectionConfiguration Configuration() => new()
    {
        Version = 2,
        People = [new PersonConfiguration { Id = "person", Name = "Person" }],
        Sources = [Source("source", null)]
    };

    private static WatchedSourceConfiguration Source(string id, string? profileId) => new()
    {
        Id = id,
        DocumentProfileId = profileId,
        Coverage = [new SourceCoverageConfiguration { PersonId = "person", StartsOn = new DateOnly(2020, 1, 1) }],
        Url = $"https://{id}.example.test/",
        AllowedOrigin = $"https://{id}.example.test",
        AllowedPathPrefix = "/"
    };
}
