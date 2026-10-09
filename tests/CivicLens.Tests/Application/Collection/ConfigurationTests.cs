using CivicLens.Application.Collection;

namespace CivicLens.Tests.Application.Collection;

public sealed class ConfigurationTests
{
    [Fact]
    public void OversizedSourceIdentityIsRejectedBeforeCollectionOrJobAdmission()
    {
        var configuration = ValidConfiguration([new PersonConfiguration { Id = "person", Name = "Official" }], ["person"]);
        configuration = configuration with { Sources = [configuration.Sources[0] with { Id = new string('s', 70_000) }] };
        Assert.Throws<ArgumentException>(configuration.Validate);
        Assert.Throws<ArgumentException>(() => CivicLens.Application.Collection.Jobs.CollectionJobDefinition
            .FromConfiguration(configuration, configuration.Sources[0].Id));
    }

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

    [Theory]
    [InlineData(null, 3600)]
    [InlineData(60, 60)]
    [InlineData(604800, 604800)]
    public void SourceCheckIntervalUsesHourlyDefaultAndAcceptsBoundaries(int? configuredSeconds, int expectedSeconds)
    {
        var configuration = ValidConfiguration([new PersonConfiguration { Id = "person", Name = "Official" }], ["person"]);
        configuration = configuration with
        {
            Sources = [configuration.Sources[0] with { CheckIntervalSeconds = configuredSeconds }]
        };

        configuration.Validate();

        Assert.Equal(expectedSeconds, configuration.Sources[0].ResolveCheckIntervalSeconds());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(59)]
    [InlineData(604801)]
    public void SourceCheckIntervalRejectsValuesOutsideSupportedRange(int configuredSeconds)
    {
        var configuration = ValidConfiguration([new PersonConfiguration { Id = "person", Name = "Official" }], ["person"]);
        configuration = configuration with
        {
            Sources = [configuration.Sources[0] with { CheckIntervalSeconds = configuredSeconds }]
        };

        Assert.Throws<ArgumentException>(configuration.Validate);
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

    [Fact]
    public void VersionTwoCoverageUsesInclusiveStartExclusiveEndAndReturnsSortedIds()
    {
        var configuration = new CollectionConfiguration
        {
            Version = 2,
            People = [
                new PersonConfiguration { Id = "z", Name = "Zed" },
                new PersonConfiguration { Id = "a", Name = "Amy" }],
            Sources = [ValidSource([]) with
            {
                PersonIds = null,
                Coverage = [
                    new SourceCoverageConfiguration { PersonId = "z", StartsOn = new DateOnly(2024, 1, 1), EndsBefore = new DateOnly(2024, 2, 1) },
                    new SourceCoverageConfiguration { PersonId = "a", EndsBefore = new DateOnly(2024, 2, 1) }]
            }]
        };

        Assert.Equal(["a", "z"], configuration.GetCoveredPersonIds("shared", new DateOnly(2024, 1, 31)));
        Assert.Equal(["a"], configuration.GetCoveredPersonIds("shared", new DateOnly(2023, 12, 31)));
        Assert.Empty(configuration.GetCoveredPersonIds("shared", new DateOnly(2024, 2, 1)));
        Assert.Throws<ArgumentException>(() => configuration.CreateRequest("shared", "job", Path.GetFullPath("captures"), new DateOnly(2024, 2, 1)));
        Assert.Equal("shared", configuration.CreateRequest("shared", "job", Path.GetFullPath("captures"), new DateOnly(2024, 1, 1)).SourceId);
    }

    [Fact]
    public void VersionTwoRejectsOverlappingCoverageButAllowsAdjacentIntervalsAndDifferentPeople()
    {
        var configuration = DatedConfiguration(
            [new SourceCoverageConfiguration { PersonId = "one", StartsOn = new DateOnly(2024, 1, 1), EndsBefore = new DateOnly(2024, 2, 1) },
             new SourceCoverageConfiguration { PersonId = "one", StartsOn = new DateOnly(2024, 2, 1), EndsBefore = new DateOnly(2024, 3, 1) },
             new SourceCoverageConfiguration { PersonId = "two", StartsOn = new DateOnly(2024, 1, 15), EndsBefore = new DateOnly(2024, 2, 15) }]);
        configuration.Validate();
        var overlapping = configuration with
        {
            Sources = [configuration.Sources[0] with
        {
            Coverage = [.. configuration.Sources[0].Coverage!, new SourceCoverageConfiguration
            {
                PersonId = "one", StartsOn = new DateOnly(2024, 1, 31), EndsBefore = new DateOnly(2024, 2, 3)
            }]
        }]
        };
        Assert.Throws<ArgumentException>(overlapping.Validate);
    }

    [Fact]
    public void VersionTwoSupportsHundredsOfPeopleOnOneSource()
    {
        var people = Enumerable.Range(1, 500)
            .Select(index => new PersonConfiguration { Id = $"person-{index}", Name = $"Official {index}" })
            .ToArray();
        var configuration = new CollectionConfiguration
        {
            Version = 2,
            People = people,
            Sources = [ValidSource([]) with
            {
                PersonIds = null,
                Coverage = people.Select(person => new SourceCoverageConfiguration { PersonId = person.Id }).ToArray()
            }]
        };

        Assert.Equal(500, configuration.GetCoveredPersonIds("shared", new DateOnly(2024, 1, 1)).Length);
        Assert.Single(configuration.Sources);
    }

    [Fact]
    public void DatedNamesAllowOverlappingDifferentNamesAndRejectDuplicateNameOverlap()
    {
        var person = new PersonConfiguration
        {
            Id = "one",
            Name = "Current",
            Names = [
                new DatedNameConfiguration { Name = "Alias", StartsOn = new DateOnly(2020, 1, 1), EndsBefore = new DateOnly(2021, 1, 1) },
                new DatedNameConfiguration { Name = "Other", StartsOn = new DateOnly(2020, 6, 1), EndsBefore = new DateOnly(2021, 6, 1) }]
        };
        DatedConfiguration([new SourceCoverageConfiguration { PersonId = "one" }], person).Validate();
        var duplicate = person with
        {
            Names = [.. person.Names!, person.Names[0] with
        {
            StartsOn = new DateOnly(2020, 12, 1), EndsBefore = new DateOnly(2021, 2, 1)
        }]
        };
        Assert.Throws<ArgumentException>(() => DatedConfiguration([new SourceCoverageConfiguration { PersonId = "one" }], duplicate).Validate());
    }

    [Fact]
    public void VersionAndCoverageShapesAreStrictEvenForDisabledSources()
    {
        var dated = DatedConfiguration([new SourceCoverageConfiguration { PersonId = "one" }]);
        Assert.Throws<ArgumentException>(() => (dated with
        {
            Sources = [dated.Sources[0] with { Enabled = false, PersonIds = ["one"] }]
        }).Validate());
        Assert.Throws<ArgumentException>(() => (dated with
        {
            People = [dated.People[0] with
        {
            Names = [null!]
        }]
        }).Validate());
        Assert.Throws<ArgumentException>(() => (dated with
        {
            Sources = [dated.Sources[0] with
        {
            Coverage = [new SourceCoverageConfiguration { PersonId = "missing" }], Enabled = false
        }]
        }).Validate());
        Assert.Throws<ArgumentException>(() => (dated with
        {
            Sources = [dated.Sources[0] with
        {
            Coverage = [new SourceCoverageConfiguration { PersonId = "one", StartsOn = new DateOnly(2024, 2, 1), EndsBefore = new DateOnly(2024, 2, 1) }], Enabled = false
        }]
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

    private static CollectionConfiguration DatedConfiguration(SourceCoverageConfiguration[] coverage,
        PersonConfiguration? person = null) => new()
        {
            Version = 2,
            People = [person ?? new PersonConfiguration { Id = "one", Name = "One" },
            new PersonConfiguration { Id = "two", Name = "Two" }],
            Sources = [ValidSource([]) with { PersonIds = null, Coverage = coverage }]
        };
}
