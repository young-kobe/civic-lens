using CivicLens.Application.Issues;
using CivicLens.Application.Publication;

namespace CivicLens.Tests.Application.Publication;

public sealed class PublicationCatalogTests
{
    private static readonly PublicationCatalog Catalog = new(new Dictionary<string, string>(), new IssueConfiguration
    {
        PolicyAreas = [new() { Id = "land-use", Name = "Land use" }],
        Issues = [new() { Id = "housing", Name = "Housing costs", Definition = "d", PolicyAreaIds = ["land-use"] }]
    });

    [Fact]
    public void OnlyAConfiguredPolicyAreaIdHasAPolicyAreaSoAPageLinksOnlyToNamedAreas()
    {
        Assert.True(Catalog.HasPolicyArea("land-use"));
        Assert.False(Catalog.HasPolicyArea("mystery"));
        Assert.False(Catalog.HasPolicyArea("housing"));
    }
}
