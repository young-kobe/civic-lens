using System.Text.Json;
using CivicLens.Application.Collection;
using CivicLens.Tests.Fixtures;
using CivicLens.Application.Configuration;
using CivicLens.Collection.Contracts;
using CivicLens.Tests.Application.Collection.Bills;

namespace CivicLens.Tests.Application.Collection;

public sealed class ConfigurationTests
{
    [Fact]
    public void OversizedSourceIdentityIsRejectedBeforeCollectionOrJobAdmission()
    {
        var configuration = ValidConfiguration([new PersonConfiguration { Id = "person", Name = "Official" }], ["person"]);
        configuration = configuration with { Sources = [configuration.Sources[0] with { Id = new string('s', 70_000) }] };
        Assert.Throws<ArgumentException>(configuration.Validate);
        Assert.Throws<ArgumentException>(() => CollectionFixtures.Definition(configuration, configuration.Sources[0].Id));
    }

    [Fact]
    public void PreparedAttemptsHaveFreshWireJobIds()
    {
        var revision = CollectionConfigurationRevision.Create(
            ValidConfiguration([new PersonConfiguration { Id = "person", Name = "Official" }], ["person"]));
        var source = new ConfiguredCollectionSource(revision, "shared");
        var first = new PreparedCollectionAttempt(source, "captures");
        var second = new PreparedCollectionAttempt(source, "captures");

        Assert.NotEqual(first.Request.JobId, second.Request.JobId);
        Assert.Equal(Path.GetFullPath("captures"), first.Request.ArtifactDirectory);
        Assert.Equal("shared", first.Request.SourceId);
        Assert.Throws<ArgumentException>(() => new ConfiguredCollectionSource(revision, "missing"));
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
        Assert.Equal(500, CoveredOnShared(configuration, new DateOnly(2024, 1, 1)).Length);
        var request = new ConfiguredCollectionSource(configuration, "shared")
            .CreateRequest("job-1", Path.GetFullPath("artifacts"), new DateOnly(2024, 1, 1));
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
    [InlineData(CollectionMode.Page)]
    [InlineData(CollectionMode.Html)]
    public void StatementTextInFeedIsRefusedOnASourceThatIsNotAFeedBecauseOnlyAFeedHoldsItemText(CollectionMode mode)
    {
        var configuration = ValidConfiguration([new PersonConfiguration { Id = "person", Name = "Official" }], ["person"]);
        configuration = configuration with { Sources = [configuration.Sources[0] with { Mode = mode, StatementTextInFeed = true }] };

        Assert.Throws<ArgumentException>(configuration.Validate);
    }

    [Fact]
    public void StatementTextInFeedIsOffByDefaultAndAnOffSourceSavesNoNewField()
    {
        var configuration = ValidConfiguration([new PersonConfiguration { Id = "person", Name = "Official" }], ["person"]);
        configuration = configuration with { Sources = [configuration.Sources[0] with { Mode = CollectionMode.Feed }] };
        configuration.Validate();

        var off = CollectionConfigurationRevision.Create(configuration);
        var on = CollectionConfigurationRevision.Create(configuration with
        {
            Sources = [configuration.Sources[0] with { StatementTextInFeed = true }]
        });

        Assert.False(configuration.Sources[0].StatementTextInFeed);
        Assert.DoesNotContain("statementTextInFeed", off.Json, StringComparison.Ordinal);
        Assert.True(on.ReadConfiguration().Sources[0].StatementTextInFeed);
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
        Assert.Throws<ArgumentException>(() => disabled.EligibleSource("shared", new DateOnly(2024, 1, 1)));
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
    public void CoverageUsesInclusiveStartExclusiveEndAndReturnsSortedIds()
    {
        var configuration = new CollectionConfiguration
        {
            Version = 1,
            People = [
                new PersonConfiguration { Id = "z", Name = "Zed" },
                new PersonConfiguration { Id = "a", Name = "Amy" }],
            Sources = [ValidSource([]) with
            {
                Coverage = [
                    new SourceCoverageConfiguration { PersonId = "z", StartsOn = new DateOnly(2024, 1, 1), EndsBefore = new DateOnly(2024, 2, 1) },
                    new SourceCoverageConfiguration { PersonId = "a", EndsBefore = new DateOnly(2024, 2, 1) }]
            }]
        };

        Assert.Equal(["a", "z"], CoveredOnShared(configuration, new DateOnly(2024, 1, 31)));
        Assert.Equal(["a"], CoveredOnShared(configuration, new DateOnly(2023, 12, 31)));
        Assert.Empty(CoveredOnShared(configuration, new DateOnly(2024, 2, 1)));
        Assert.Throws<ArgumentException>(() => configuration.EligibleSource("shared", new DateOnly(2024, 2, 1)));
        Assert.Equal("shared", configuration.EligibleSource("shared", new DateOnly(2024, 1, 1)).Id);
    }

    [Fact]
    public void RejectsOverlappingCoverageButAllowsAdjacentIntervalsAndDifferentPeople()
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

    [Theory]
    [InlineData("w000817")]
    [InlineData("W00081")]
    [InlineData("W0008177")]
    [InlineData("WX00817")]
    [InlineData(" W000817")]
    [InlineData("W000817\n")]
    [InlineData("")]
    public void BioguideIdMustBeOneUppercaseLetterAndSixDigits(string bioguideId)
    {
        var person = new PersonConfiguration { Id = "one", Name = "One", BioguideId = bioguideId };

        Assert.Throws<ArgumentException>(() => DatedConfiguration([new SourceCoverageConfiguration { PersonId = "one" }], person).Validate());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("W000817")]
    public void BioguideIdIsOptionalAndAccepted(string? bioguideId)
    {
        var person = new PersonConfiguration { Id = "one", Name = "One", BioguideId = bioguideId };

        DatedConfiguration([new SourceCoverageConfiguration { PersonId = "one" }], person).Validate();
    }

    [Theory]
    [InlineData("q22686")]
    [InlineData("Q")]
    [InlineData("Q0")]
    [InlineData("Q022686")]
    [InlineData("Q22686a")]
    [InlineData(" Q22686")]
    [InlineData("Q22686\n")]
    [InlineData("")]
    public void WikidataIdMustBeQAndDigitsWithoutALeadingZero(string wikidataId)
    {
        var person = new PersonConfiguration { Id = "one", Name = "One", WikidataId = wikidataId };

        Assert.Throws<ArgumentException>(() => DatedConfiguration([new SourceCoverageConfiguration { PersonId = "one" }], person).Validate());
    }

    [Theory]
    [InlineData("http://example.gov")]
    [InlineData("https://user@example.gov")]
    [InlineData("/relative")]
    [InlineData("")]
    [InlineData("https:example.gov")]
    [InlineData("https://example.gov ")]
    public void AWebsiteMustBeAnAbsoluteHttpsUrlWithNoUserInfo(string website)
    {
        var person = new PersonConfiguration { Id = "one", Name = "One", Website = website };

        Assert.Throws<ArgumentException>(() => DatedConfiguration([new SourceCoverageConfiguration { PersonId = "one" }], person).Validate());
    }

    [Fact]
    public void AWebsiteOverTheLengthCapIsRefused()
    {
        var person = new PersonConfiguration { Id = "one", Name = "One", Website = "https://example.gov/" + new string('a', 600) };

        Assert.Throws<ArgumentException>(() => DatedConfiguration([new SourceCoverageConfiguration { PersonId = "one" }], person).Validate());
    }

    [Fact]
    public void AnHttpsWebsiteIsAccepted()
    {
        var person = new PersonConfiguration { Id = "one", Name = "One", Website = "https://example.gov/" };

        DatedConfiguration([new SourceCoverageConfiguration { PersonId = "one" }], person).Validate();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Q22686")]
    [InlineData("Q1")]
    public void WikidataIdIsOptional(string? wikidataId)
    {
        var person = new PersonConfiguration { Id = "one", Name = "One", WikidataId = wikidataId };

        DatedConfiguration([new SourceCoverageConfiguration { PersonId = "one" }], person).Validate();
    }

    [Theory]
    [InlineData("Upper")]
    [InlineData("has space")]
    [InlineData("a/b")]
    [InlineData("-lead")]
    public void APersonIdMustBeAPathSegmentBecauseTheOfficialPageUsesItAsAnAddress(string id)
    {
        var person = new PersonConfiguration { Id = id, Name = "One" };

        Assert.Throws<ArgumentException>(() => DatedConfiguration([new SourceCoverageConfiguration { PersonId = id }], person).Validate());
    }

    [Fact]
    public void AnOfficeNeedsATitleADateAndAnEndThatDoesNotPrecedeItsStart()
    {
        var valid = new OfficeConfiguration { Title = "Secretary of State", StartsOn = new(2025, 1, 21), Party = "Republican" };
        var persons = new[]
        {
            new PersonConfiguration { Id = "one", Name = "One", Offices = [valid with { Title = " " }] },
            new PersonConfiguration { Id = "one", Name = "One", Offices = [valid with { Title = new string('t', 257) }] },
            new PersonConfiguration { Id = "one", Name = "One", Offices = [valid with { Party = new string('p', 257) }] },
            new PersonConfiguration { Id = "one", Name = "One", Offices = [valid with { EndsOn = new DateOnly(2025, 1, 20) }] },
            new PersonConfiguration { Id = "one", Name = "One", Offices = [.. Enumerable.Repeat(valid, OfficeConfiguration.MaximumPerPerson + 1)] }
        };

        foreach (var person in persons)
            Assert.Throws<ArgumentException>(() => DatedConfiguration([new SourceCoverageConfiguration { PersonId = "one" }], person).Validate());
        DatedConfiguration([new SourceCoverageConfiguration { PersonId = "one" }],
            new PersonConfiguration { Id = "one", Name = "One", Offices = [valid, valid with { EndsOn = valid.StartsOn }] }).Validate();
    }

    [Fact]
    public void AnOfficeIsAllowedForAPersonWithABioguideIdBecauseAFormerMemberHasNoRosterTerm()
    {
        var person = new PersonConfiguration
        {
            Id = "one",
            Name = "One",
            BioguideId = "W000817",
            Offices = [new() { Title = "Vice President", StartsOn = new(2025, 1, 20), EndsOn = new(2029, 1, 20) }]
        };

        DatedConfiguration([new SourceCoverageConfiguration { PersonId = "one" }], person).Validate();
    }

    [Fact]
    public void TwoPeopleCannotShareOneWikidataIdBecauseItNamesOnePerson()
    {
        var configuration = DatedConfiguration([new SourceCoverageConfiguration { PersonId = "one" }],
            new PersonConfiguration { Id = "one", Name = "One", WikidataId = "Q22686" });
        var shared = configuration with
        {
            People = [configuration.People[0], configuration.People[1] with { WikidataId = "Q22686" }]
        };

        Assert.Throws<ArgumentException>(shared.Validate);
    }

    [Fact]
    public void TwoPeopleCannotShareOneBioguideIdBecauseItNamesOneMember()
    {
        var configuration = DatedConfiguration([new SourceCoverageConfiguration { PersonId = "one" }],
            new PersonConfiguration { Id = "one", Name = "One", BioguideId = "W000817" });
        var shared = configuration with
        {
            People = [configuration.People[0], configuration.People[1] with { BioguideId = "W000817" }]
        };

        Assert.Throws<ArgumentException>(shared.Validate);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(3)]
    public void OnlyConfigurationVersionOneIsSupported(int version)
    {
        var configuration = ValidConfiguration([new PersonConfiguration { Id = "person", Name = "Official" }], ["person"]);

        Assert.Throws<ArgumentException>((configuration with { Version = version }).Validate);
    }

    [Fact]
    public void LegacyPersonIdsMembershipIsRejectedByTheWireContract()
    {
        const string json = """
            {"version":1,"people":[{"id":"person","name":"Official"}],"sources":[{"id":"shared","personIds":["person"],
            "url":"http://127.0.0.1:8765/page.html","allowedOrigin":"http://127.0.0.1:8765","allowedPathPrefix":"/"}]}
            """;

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<CollectionConfiguration>(json, ConfigurationJson.Options));
    }

    [Theory]
    [InlineData("executive", ConfiguredOfficeKind.Executive)]
    [InlineData("judicial", ConfiguredOfficeKind.Judicial)]
    public void AnOfficeKindIsReadFromItsClosedSet(string kind, ConfiguredOfficeKind expected)
    {
        var office = JsonSerializer.Deserialize<OfficeConfiguration>($$"""{"title":"Chief Justice","startsOn":"2005-09-29","kind":"{{kind}}"}""", ConfigurationJson.Options);

        Assert.Equal(expected, office!.Kind);
    }

    [Theory]
    [InlineData("legislative")]
    [InlineData("justice")]
    [InlineData("1")]
    public void AnOfficeKindOutsideTheClosedSetIsRejectedAtLoadBecauseItWouldSendAnOfficialToTheWrongTab(string kind)
    {
        var json = $$"""{"title":"Chief Justice","startsOn":"2005-09-29","kind":"{{kind}}"}""";

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<OfficeConfiguration>(json, ConfigurationJson.Options));
    }

    [Fact]
    public void CoverageShapesAreStrictEvenForDisabledSources()
    {
        var dated = DatedConfiguration([new SourceCoverageConfiguration { PersonId = "one" }]);
        Assert.Throws<ArgumentException>(() => (dated with
        {
            Sources = [dated.Sources[0] with { Enabled = false, Coverage = null }]
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

    [Fact]
    public void AcceptsAnApiSourceWithANamedCredentialAndPassesItToTheRequest()
    {
        var configuration = ApiConfiguration(CollectionCredentials.CongressGov);

        configuration.Validate();
        var request = new ConfiguredCollectionSource(configuration, "shared")
            .CreateRequest("job", Path.GetFullPath("captures"), DateOnly.FromDateTime(DateTime.UtcNow));

        Assert.Equal(CollectionMode.Api, request.Mode);
        Assert.Equal(CollectionCredentials.CongressGov, request.Credential);
    }

    [Theory]
    [InlineData(CollectionMode.Page, CollectionCredentials.CongressGov)]
    [InlineData(CollectionMode.Api, "unknownService")]
    public void CredentialNeedsApiModeAndAKnownName(CollectionMode mode, string credential)
    {
        var configuration = ApiConfiguration(credential);

        Assert.Throws<ArgumentException>((configuration with
        {
            Sources = [configuration.Sources[0] with { Mode = mode }]
        }).Validate);
    }

    [Fact]
    public void ApiModeNeedsAnApiParser()
    {
        var configuration = ApiConfiguration(CollectionCredentials.CongressGov);

        var failure = Assert.Throws<ArgumentException>((configuration with
        {
            Sources = [configuration.Sources[0] with { ApiParser = null, MinimumCongress = null, BackfillFrom = null }]
        }).Validate);

        Assert.Contains("needs an API parser", failure.Message, StringComparison.Ordinal);
    }

    private static CollectionConfiguration ApiConfiguration(string credential)
    {
        var configuration = CongressBillSourceConfigurationTests.Configuration();
        return configuration with
        {
            Sources = [configuration.Sources[0] with { Id = "shared", Credential = credential }]
        };
    }

    private static CollectionConfiguration ValidConfiguration(PersonConfiguration[] people, string[] personIds) =>
        new() { People = people, Sources = [ValidSource(personIds)] };

    private static WatchedSourceConfiguration ValidSource(string[] personIds) => new()
    {
        Id = "shared",
        Coverage = personIds?.Select(personId => new SourceCoverageConfiguration { PersonId = personId }).ToArray(),
        Url = "http://127.0.0.1:8765/page.html",
        AllowedOrigin = "http://127.0.0.1:8765",
        AllowedPathPrefix = "/"
    };

    private static CollectionConfiguration DatedConfiguration(SourceCoverageConfiguration[] coverage,
        PersonConfiguration? person = null) => new()
        {
            Version = 1,
            People = [person ?? new PersonConfiguration { Id = "one", Name = "One" },
            new PersonConfiguration { Id = "two", Name = "Two" }],
            Sources = [ValidSource([]) with { Coverage = coverage }]
        };

    private static string[] CoveredOnShared(CollectionConfiguration configuration, DateOnly day) =>
        configuration.CoveredPersonIdsOf(configuration.Sources.Single(source => source.Id == "shared"), day);
}
