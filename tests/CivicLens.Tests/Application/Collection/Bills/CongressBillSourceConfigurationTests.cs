using System.Security.Cryptography;
using System.Text;
using CivicLens.Application.Collection;
using CivicLens.Application.Collection.Bills;
using CivicLens.Application.Collection.Jobs;
using CivicLens.Collection.Contracts;

namespace CivicLens.Tests.Application.Collection.Bills;

public sealed class CongressBillSourceConfigurationTests
{
    private static readonly DateOnly Backfill = new(2025, 1, 3);

    [Fact]
    public void BillListSourceCoversExactlyThePeopleWithBioguideIdsFromTheBackfillDate()
    {
        var configuration = Configuration();
        var source = configuration.Sources.Single();

        Assert.Equal(["with-id-a", "with-id-b"], configuration.CoveredPersonIdsOf(source, Backfill));
        Assert.Equal(["with-id-a", "with-id-b"], configuration.GetCoveredPersonIds("congress-bills", new DateOnly(2030, 1, 1)));
        Assert.Empty(configuration.CoveredPersonIdsOf(source, Backfill.AddDays(-1)));
    }

    [Fact]
    public void BillListSourceNamesNoPeopleSoLabelsDoNotListEveryMember()
    {
        var configuration = Configuration();

        Assert.Empty(configuration.ListedPersonIdsOf(configuration.Sources.Single()));
        Assert.Equal(["with-id-a"], configuration.ListedPersonIdsOf(configuration.Sources.Single() with
        {
            ApiParser = null,
            MinimumCongress = null,
            BackfillFrom = null,
            Coverage = [new SourceCoverageConfiguration { PersonId = "with-id-a" }]
        }));
    }

    [Fact]
    public void RequestsAreCreatedOnlyWhileSomeoneWithABioguideIdIsCovered()
    {
        var configuration = Configuration();

        Assert.Equal(CollectionMode.Api, new ConfiguredCollectionSource(configuration, "congress-bills")
            .CreateRequest("job", Path.GetFullPath("captures"), Backfill).Mode);
        Assert.Throws<ArgumentException>(() => configuration.EligibleSource("congress-bills", Backfill.AddDays(-1)));
        Assert.Throws<ArgumentException>(() => (configuration with { People = [configuration.People[2]] })
            .EligibleSource("congress-bills", Backfill));
    }

    [Fact]
    public void ParserOptionsSurviveARevisionRoundTrip()
    {
        var source = CollectionConfigurationRevision.Create(Configuration()).ReadConfiguration().Sources.Single();

        Assert.Equal(ApiParser.CongressBillList, source.ApiParser);
        Assert.Equal(119, source.MinimumCongress);
        Assert.Equal(Backfill, source.BackfillFrom);
    }

    [Fact]
    public void ConfigurationWithoutParserOptionsKeepsItsSnapshotJsonAndIdentity()
    {
        var configuration = new CollectionConfiguration
        {
            Version = 2,
            People = [new PersonConfiguration { Id = "p", Name = "P" }],
            Sources =
            [
                new WatchedSourceConfiguration
                {
                    Id = "s",
                    Coverage = [new SourceCoverageConfiguration { PersonId = "p" }],
                    Url = "https://example.test/a",
                    AllowedOrigin = "https://example.test",
                    AllowedPathPrefix = "/"
                }
            ]
        };
        const string expected = """{"version":2,"people":[{"id":"p","name":"P"}],"sources":[{"id":"s","coverage":[{"personId":"p"}],"url":"https://example.test/a","allowedOrigin":"https://example.test","allowedPathPrefix":"/","mode":"page","maxCandidates":100,"enabled":true,"maxRequests":5,"maxBytes":2000000,"timeoutSeconds":30,"minDelayMilliseconds":1000}]}""";

        var revision = CollectionConfigurationRevision.Create(configuration);

        Assert.Equal(expected, revision.Json);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(expected))), revision.Id);
    }

    [Fact]
    public void ABillSourceRejectsAListOfPeopleBecauseItAlreadyCoversEveryBioguideId()
    {
        var configuration = Configuration();
        var source = configuration.Sources.Single();

        Assert.Throws<ArgumentException>((configuration with
        {
            Sources = [source with { Coverage = [new SourceCoverageConfiguration { PersonId = "with-id-a" }] }]
        }).Validate);
    }

    [Fact]
    public void OtherVersionTwoSourcesStillNeedCoverage()
    {
        var configuration = Configuration();

        Assert.Throws<ArgumentException>((configuration with
        {
            Sources = [configuration.Sources.Single() with { ApiParser = null, MinimumCongress = null, BackfillFrom = null }]
        }).Validate);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(null, true)]
    [InlineData(119, false)]
    public void MinimumCongressAndBackfillDateAreRequiredWithTheParser(int? minimumCongress, bool hasBackfill)
    {
        var configuration = Configuration();
        var source = configuration.Sources.Single() with
        {
            MinimumCongress = minimumCongress,
            BackfillFrom = hasBackfill ? Backfill : null
        };

        Assert.Throws<ArgumentException>((configuration with { Sources = [source] }).Validate);
    }

    [Fact]
    public void ParserNeedsACredentialAndApiMode()
    {
        var configuration = Configuration();
        var source = configuration.Sources.Single();

        Assert.Throws<ArgumentException>((configuration with { Sources = [source with { Credential = null }] }).Validate);
        Assert.Throws<ArgumentException>((configuration with { Sources = [source with { Mode = CollectionMode.Page }] }).Validate);
    }

    [Theory]
    [InlineData(250, 1506, 753_000_000, 15_060)]
    [InlineData(251, 1505, 753_000_000, 15_060)]
    [InlineData(251, 1506, 752_999_999, 15_060)]
    [InlineData(251, 1506, 753_000_000, 15_059)]
    public void AdmissionPolicyMustHoldAFullPageOfDetailJobsAndTheNextPageJob(int maxJobs, int requests, long bytes, int seconds)
    {
        var configuration = Configuration();
        var source = configuration.Sources.Single();
        var policy = source.AdmissionPolicy! with
        {
            MaxJobs = maxJobs,
            MaxTotalRequests = requests,
            MaxTotalBytes = bytes,
            MaxTotalTimeoutSeconds = seconds
        };

        Assert.Throws<ArgumentException>((configuration with { Sources = [source with { AdmissionPolicy = policy }] }).Validate);
        Assert.Throws<ArgumentException>((configuration with { Sources = [source with { AdmissionPolicy = null }] }).Validate);
        configuration.Validate();
    }

    [Fact]
    public void ParserOptionsWithoutAParserAreRejectedBecauseNothingWouldReadThem()
    {
        var configuration = Configuration();
        var source = configuration.Sources.Single() with
        {
            ApiParser = null,
            Coverage = [new SourceCoverageConfiguration { PersonId = "with-id-a" }]
        };

        Assert.Throws<ArgumentException>((configuration with { Sources = [source] }).Validate);
        Assert.Throws<ArgumentException>((configuration with { Sources = [source with { MinimumCongress = null }] }).Validate);
    }

    [Fact]
    public void WindowAndDetailJobsStayBoundToTheSourceWhileOtherUrlsAreRejected()
    {
        var configured = new ConfiguredCollectionSource(Configuration(), "congress-bills", Backfill);
        var window = new BillStreamWindow(Backfill, Backfill);

        var listJob = configured.CreateApiJobDefinition(window.RequestUrl("https://api.congress.gov/v3/bill"), Backfill);
        var detailJob = configured.CreateApiJobDefinition("https://api.congress.gov/v3/bill/119/hr/1?format=json", Backfill);

        Assert.Equal(CollectionMode.Api, listJob.Mode);
        Assert.Equal(CollectionCredentials.CongressGov, detailJob.Credential);
        Assert.Throws<ArgumentException>(() => configured.CreateApiJobDefinition("https://api.congress.gov/v3/member", Backfill));
    }

    [Fact]
    public void ConfiguredSourcesWithoutAParserRefuseBillAdmission()
    {
        var configuration = Configuration();
        var plain = configuration with
        {
            Sources = [configuration.Sources.Single() with
            {
                ApiParser = null,
                MinimumCongress = null,
                BackfillFrom = null,
                Coverage = [new SourceCoverageConfiguration { PersonId = "with-id-a" }]
            }]
        };

        Assert.Throws<ArgumentException>(() => new ConfiguredCollectionSource(plain, "congress-bills", Backfill)
            .CreateBillStreamAdmission("job", new BillStreamWindow(Backfill, Backfill), new CongressBillListPage([], null), Backfill));
    }

    internal static CollectionConfiguration Configuration() => new()
    {
        Version = 2,
        People =
        [
            new PersonConfiguration { Id = "with-id-a", Name = "A", BioguideId = "A000001" },
            new PersonConfiguration { Id = "with-id-b", Name = "B", BioguideId = "B000002" },
            new PersonConfiguration { Id = "without-id", Name = "C" }
        ],
        Sources =
        [
            new WatchedSourceConfiguration
            {
                Id = "congress-bills",
                Url = "https://api.congress.gov/v3/bill",
                AllowedOrigin = "https://api.congress.gov",
                AllowedPathPrefix = "/v3/bill",
                Mode = CollectionMode.Api,
                Credential = CollectionCredentials.CongressGov,
                ApiParser = ApiParser.CongressBillList,
                MinimumCongress = 119,
                BackfillFrom = Backfill,
                AdmissionPolicy = new CivicLens.Application.Collection.Discovery.DiscoveryAdmissionPolicy
                {
                    MaxJobs = 251,
                    MaxTotalRequests = 1506,
                    MaxTotalBytes = 753_000_000,
                    MaxTotalTimeoutSeconds = 15_060
                },
                MaxRequests = 2,
                MaxBytes = 1_000_000,
                TimeoutSeconds = 20
            }
        ]
    };
}
