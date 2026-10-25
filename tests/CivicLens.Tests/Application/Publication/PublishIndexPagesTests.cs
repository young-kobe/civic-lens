using System.Text;
using CivicLens.Application.Issues;
using CivicLens.Application.Publication;
using CivicLens.Publication.Contracts;

namespace CivicLens.Tests.Application.Publication;

public sealed class PublishIndexPagesTests
{
    [Fact]
    public async Task AnIssuePageListsOnlyTheRecordsThatHoldItsIdSoReadersDoNotSeeOtherTopics()
    {
        var scenario = new PublicationScenario();
        scenario.SeedRelease(withFiles: true, [Seeded(0, issueIds: ["transit"]), Seeded(1, issueIds: ["housing"])]);
        var draft = scenario.AddDraft('a');

        var summary = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k", draft));

        Assert.Equal($"<html>facet housing 1 {draft},{1.ToString("x32")}</html>", PageText(scenario, summary.DirectoryName, "issues/housing.html"));
        Assert.Equal($"<html>facet transit 1 {0.ToString("x32")}</html>", PageText(scenario, summary.DirectoryName, "issues/transit.html"));
    }

    [Fact]
    public async Task AnIssuePageShowsTheConfiguredNameAndAnUnconfiguredIdShowsItselfSoACarriedRecordNeverBlocksAPublish()
    {
        var scenario = new PublicationScenario
        {
            Issues = new IssueConfiguration
            {
                PolicyAreas = [new() { Id = "land-use", Name = "Land use" }],
                Issues = [new() { Id = "housing", Name = "Housing costs", Definition = "d", PolicyAreaIds = ["land-use"] }]
            }
        };
        scenario.SeedRelease(withFiles: true, [Seeded(0, issueIds: ["retired-issue"], policyAreaIds: ["land-use", "retired-area"])]);

        var summary = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner,
            PublicationScenario.Request("k", scenario.AddDraft('a')));

        Assert.StartsWith("<html>facet Housing costs 1", PageText(scenario, summary.DirectoryName, "issues/housing.html"), StringComparison.Ordinal);
        Assert.StartsWith("<html>facet retired-issue 1", PageText(scenario, summary.DirectoryName, "issues/retired-issue.html"), StringComparison.Ordinal);
        Assert.StartsWith("<html>facet Land use 1", PageText(scenario, summary.DirectoryName, "policy-areas/land-use.html"), StringComparison.Ordinal);
        Assert.StartsWith("<html>facet retired-area 1", PageText(scenario, summary.DirectoryName, "policy-areas/retired-area.html"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AFacetIdThatCannotBeAPathSegmentGetsNoPageAndDoesNotFailThePublish()
    {
        var scenario = new PublicationScenario();
        scenario.SeedRelease(withFiles: true, [Seeded(0, issueIds: ["Not A Path/Id", new string('x', PublicationProtocol.MaximumFacetIdLength + 1)])]);

        var summary = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner,
            PublicationScenario.Request("k", scenario.AddDraft('a')));

        Assert.DoesNotContain(scenario.Releases.Directories[summary.DirectoryName].Keys, path => path.StartsWith("issues/", StringComparison.Ordinal) &&
            path != "issues/housing.html");
        Assert.Contains("<html>facets 3 0</html>", PageText(scenario, summary.DirectoryName, PublicationProtocol.FacetsPath));
    }

    [Fact]
    public async Task EveryPublishRebuildsTheFacetPagesSoTheyListTheCarriedRecordsAndTheNewOne()
    {
        var scenario = new PublicationScenario();
        var first = scenario.AddDraft('a');
        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k1", first));
        var second = scenario.AddDraft('b');

        var summary = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k2", second));

        Assert.Equal($"<html>facet housing 1 {first},{second}</html>", PageText(scenario, summary.DirectoryName, "issues/housing.html"));
    }

    [Fact]
    public async Task AFacetPageSetStaysAtOnePagePerFiftyRecordsSoThePageCountIsBounded()
    {
        var scenario = new PublicationScenario();
        scenario.SeedRelease(withFiles: true, [.. Enumerable.Range(0, 119).Select(index => Seeded(index, issueIds: ["housing"]))]);

        var summary = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner,
            PublicationScenario.Request("k", scenario.AddDraft('a')));

        var housing = scenario.Releases.Directories[summary.DirectoryName].Keys.Where(path => path.StartsWith("issues/housing", StringComparison.Ordinal));
        Assert.Equal(["issues/housing.html", "issues/housing.page-2.html", "issues/housing.page-3.html"], housing.Order());
    }

    [Fact]
    public async Task HomeShowsTheBriefOfTheNewestWeekEvenWhenAnOlderWeekIsPublishedLater()
    {
        var scenario = new PublicationScenario();
        var statement = scenario.AddStatement('c');
        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner,
            PublicationScenario.Request("k1", new PublishedRecordReference(PublishedRecordKind.Statement, statement)));
        var newer = scenario.AddBrief(new DateOnly(2026, 5, 11), [new(PublishedRecordKind.Statement, statement, 1)]);
        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k2",
            new PublishedRecordReference(PublishedRecordKind.Brief, newer)));
        scenario.Clock.Now = scenario.Clock.Now.AddDays(1);
        var older = scenario.AddBrief(new DateOnly(2026, 5, 4), [new(PublishedRecordKind.Statement, statement, 1)]);
        var withOlder = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k3",
            new PublishedRecordReference(PublishedRecordKind.Brief, older)));
        var carried = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner,
            PublicationScenario.Request("k4", scenario.AddDraft('a')));

        Assert.Contains($"brief {newer}", PageText(scenario, withOlder.DirectoryName, PublicationProtocol.HomePath));
        Assert.Contains($"brief {newer}", PageText(scenario, carried.DirectoryName, PublicationProtocol.HomePath));
    }

    [Fact]
    public async Task HomeHasNoBriefBeforeTheFirstBriefIsPublished()
    {
        var scenario = new PublicationScenario();

        var summary = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner,
            PublicationScenario.Request("k", scenario.AddDraft('a')));

        Assert.Equal("<html>home 1 brief </html>", PageText(scenario, summary.DirectoryName, PublicationProtocol.HomePath));
    }

    private static string PageText(PublicationScenario scenario, string directory, string path) =>
        Encoding.UTF8.GetString(scenario.Releases.Directories[directory][path]);

    private static PublishedRecordEntry Seeded(int index, string[]? issueIds = null, string[]? policyAreaIds = null) => new()
    {
        Kind = PublishedRecordKind.DocumentChange,
        Officials = [],
        RecordId = index.ToString("x32"),
        RevisionNumber = 1,
        Headline = $"Seeded {index}",
        FirstPublishedAtUtc = PublicationScenario.ApprovedAt.AddMinutes(-index - 10),
        IssueIds = issueIds ?? [],
        PolicyAreaIds = policyAreaIds ?? []
    };
}
