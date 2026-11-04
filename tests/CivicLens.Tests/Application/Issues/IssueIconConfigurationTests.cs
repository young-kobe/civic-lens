using System.Text.Json;
using CivicLens.Application.Issues;
using CivicLens.Collection.Contracts;
using CivicLens.Tests.Fixtures;

namespace CivicLens.Tests.Application.Issues;

public sealed class IssueIconConfigurationTests
{
    [Fact]
    public void EveryShippedIssueNamesAnIconSoNoTileFallsBackToTheNeutralOne()
    {
        var issues = Shipped<IssueConfiguration>("issues.json");
        var icons = Shipped<IssueIconConfiguration>("issue-icons.json");

        icons.Validate(issues);

        Assert.Equal(issues.Issues.Select(issue => issue.Id).Order(StringComparer.Ordinal), icons.Icons.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void AnUnknownIconNameIsRefusedSoAnIssueCannotShipABrokenTile()
    {
        var error = Assert.Throws<ArgumentException>(() => Icons(("rent", "not-an-icon")).Validate(Issues()));

        Assert.Contains("unknown icon 'not-an-icon'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnIconForAnIssueThatDoesNotExistIsRefusedSoARenamedIssueCannotLeaveAStaleEntry()
    {
        var error = Assert.Throws<ArgumentException>(() => Icons(("gone", "bolt")).Validate(Issues()));

        Assert.Contains("unknown issue 'gone'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AKnownIconOnAKnownIssueIsAccepted() => Icons(("rent", "bolt")).Validate(Issues());

    [Fact]
    public void AnUnsupportedVersionIsRefused() =>
        Assert.Throws<ArgumentException>(() => (Icons() with { Version = 2 }).Validate(Issues()));

    [Fact]
    public void ATrackedIssueRefusesAnIconFieldSoTheIconCanNeverEnterTheIssueRevision()
    {
        const string json = """{"id":"rent","name":"Rent","definition":"d","policyAreaIds":["housing"],"icon":"bolt"}""";

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<TrackedIssueConfiguration>(json, CollectionProtocol.JsonOptions));
    }

    private static T Shipped<T>(string file) =>
        JsonSerializer.Deserialize<T>(File.ReadAllText(Path.Combine(RepositoryRoot.FullName, "config", file)), CollectionProtocol.JsonOptions)!;

    private static IssueIconConfiguration Icons(params (string Issue, string Icon)[] icons) =>
        new() { Icons = icons.ToDictionary(pair => pair.Issue, pair => pair.Icon) };

    private static IssueConfiguration Issues() => new()
    {
        PolicyAreas = [new() { Id = "housing", Name = "Housing" }],
        Issues = [new() { Id = "rent", Name = "Rent", Definition = "d", PolicyAreaIds = ["housing"] }]
    };
}
