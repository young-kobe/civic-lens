using CivicLens.Application.Collection;

namespace CivicLens.Tests.Application.Collection;

public sealed class ConfigurationTests
{
    [Fact]
    public void PreparedAttemptsHaveFreshIdentitiesSeparateFromWireJobs()
    {
        var configuration = ValidConfiguration([new PersonConfiguration { Id = "person", Name = "Official" }], ["person"]);
        var first = new PreparedCollectionAttempt(configuration, "shared", "captures");
        var second = new PreparedCollectionAttempt(configuration, "shared", "captures");

        Assert.Equal(4, new[] { first.AttemptId, first.Request.JobId, second.AttemptId, second.Request.JobId }.Distinct().Count());
        Assert.Equal(Path.GetFullPath("captures"), first.Request.ArtifactDirectory);
        Assert.Equal("shared", first.Request.SourceId);
        Assert.Throws<ArgumentException>(() => new PreparedCollectionAttempt(configuration, "missing", "captures"));
    }

    [Fact]
    public void ManyPeopleCanShareOneWatchedSourceWithoutCreatingPerPersonRequests()
    {
        var people = Enumerable.Range(1, 500)
            .Select(index => new PersonConfiguration { Id = $"person-{index}", Name = $"Official {index}" })
            .ToArray();
        var configuration = ValidConfiguration(people, people.Select(person => person.Id).ToArray());

        configuration.Validate();

        Assert.Single(configuration.Sources);
        var request = configuration.CreateRequest("shared", "job-1", Path.GetFullPath("artifacts"));
        Assert.Equal("shared", request.SourceId);
    }

    [Fact]
    public void DuplicateIdsAndDuplicateUrlsAreRejected()
    {
        var duplicatePeople = ValidConfiguration(
            [new PersonConfiguration { Id = "same", Name = "One" }, new PersonConfiguration { Id = "same", Name = "Two" }],
            ["same"]);
        Assert.Throws<ArgumentException>(duplicatePeople.Validate);

        var duplicateSources = ValidConfiguration(
            [new PersonConfiguration { Id = "one", Name = "One" }], ["one"]);
        duplicateSources = duplicateSources with
        {
            Sources = [duplicateSources.Sources[0], duplicateSources.Sources[0] with { Id = "other" }]
        };
        Assert.Throws<ArgumentException>(duplicateSources.Validate);
    }

    [Fact]
    public void DisabledSourcesStillRequireValidMembershipAndRequestScope()
    {
        var missingPerson = ValidConfiguration(
            [new PersonConfiguration { Id = "one", Name = "One" }], ["missing"]) with
        {
            Sources = [ValidSource(["missing"]) with { Enabled = false }]
        };
        Assert.Throws<ArgumentException>(missingPerson.Validate);

        var invalidScope = ValidConfiguration(
            [new PersonConfiguration { Id = "one", Name = "One" }], ["one"]) with
        {
            Sources = [ValidSource(["one"]) with { Enabled = false, AllowedPathPrefix = "/other" }]
        };
        Assert.Throws<ArgumentException>(invalidScope.Validate);
        var disabled = ValidConfiguration(
            [new PersonConfiguration { Id = "one", Name = "One" }], ["one"]) with
        {
            Sources = [ValidSource(["one"]) with { Enabled = false }]
        };
        Assert.Throws<ArgumentException>(() => disabled
            .CreateRequest("shared", "job-1", Path.GetFullPath("artifacts")));
    }

    [Fact]
    public void NullArraysAndItemsFailWithArgumentException()
    {
        Assert.Throws<ArgumentException>(() => new CollectionConfiguration { People = null!, Sources = [] }.Validate());
        Assert.Throws<ArgumentException>(() => new CollectionConfiguration { People = [null!], Sources = [] }.Validate());
        Assert.Throws<ArgumentException>(() => (ValidConfiguration(
            [new PersonConfiguration { Id = "one", Name = "One" }], ["one"]) with
        {
            Sources = [null!]
        }).Validate());
        Assert.Throws<ArgumentException>(() => (ValidConfiguration(
            [new PersonConfiguration { Id = "one", Name = "One" }], ["one"]) with
        {
            Sources = [ValidSource(null!)]
        }).Validate());
    }

    private static CollectionConfiguration ValidConfiguration(PersonConfiguration[] people, string[] personIds) =>
        new() { People = people, Sources = [ValidSource(personIds)] };

    private static WatchedSourceConfiguration ValidSource(string[] personIds) => new()
    {
        Id = "shared",
        PersonIds = personIds,
        Url = "http://127.0.0.1:8765/page.html",
        AllowedOrigin = "http://127.0.0.1:8765",
        AllowedPathPrefix = "/"
    };
}
