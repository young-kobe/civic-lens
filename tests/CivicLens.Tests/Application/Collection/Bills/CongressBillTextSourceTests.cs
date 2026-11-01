using System.Text.Json;
using CivicLens.Application.Collection;
using CivicLens.Application.Collection.Bills;
using CivicLens.Application.Collection.Discovery;
using CivicLens.Application.Collection.Jobs;
using CivicLens.Application.Configuration;
using CivicLens.Collection.Contracts;
using CivicLens.Tests.Fixtures;
using static CivicLens.Tests.Fixtures.BillTextFixtures;

namespace CivicLens.Tests.Application.Collection.Bills;

public sealed class CongressBillTextSourceTests
{
    private static readonly DateOnly Today = new(2026, 10, 28);
    private static readonly DateOnly BillDay = new(2026, 10, 20);
    private static readonly CongressBillListItem Bill = new(119, "hr", 7, BillDay);

    private static ConfiguredCollectionSource TextSource(bool enabled = true) => new(Configuration(enabled), SourceId, Today);

    private static ConfiguredCollectionSource BillsSource(CollectionConfiguration? configuration = null) =>
        new(configuration ?? Configuration(), "congress-bills", Today);

    private static CollectionConfiguration WithTextSource(WatchedSourceConfiguration source) =>
        CongressBillSourceConfigurationTests.Configuration() with
        {
            Sources = [.. CongressBillSourceConfigurationTests.Configuration().Sources, source]
        };

    [Fact]
    public void ADisabledTextSourceYieldsNoTextSourceSoNoJobIsMade()
    {
        Assert.Null(BillsSource(Configuration(enabled: false)).EnabledBillTextSource());
        Assert.Null(BillsSource(CongressBillSourceConfigurationTests.Configuration()).EnabledBillTextSource());
        Assert.Equal(SourceId, BillsSource().EnabledBillTextSource()!.SourceId);
    }

    [Fact]
    public void ADisabledTextSourceRefusesToBuildAJobDefinitionSoNoPathAroundTheGateExists()
    {
        var disabled = TextSource(enabled: false);

        Assert.Throws<ArgumentException>(() => disabled.CreateBillTextJob("versions-job", PackageId, BillDay, Today));
    }

    [Fact]
    public void TheTextJobFetchesOnePackageOfTheGovinfoHostWithTheCongressCredentialAndItsOwnKeyAndOrigin()
    {
        var job = TextSource().CreateBillTextJob("versions-job", PackageId, BillDay, Today);

        Assert.Equal("bill-stream/congress-bill-text/text/versions-job", job.IdempotencyKey);
        Assert.Equal((ApiResource.BillText, PackageUrl(), CollectionCredentials.CongressGov, "https://api.govinfo.gov"),
            (job.Definition.ApiResource, job.Definition.Url, job.Definition.Credential, job.Definition.AllowedOrigin));
        Assert.Equal("versions-job", BillStreamJobKeys.ParentJobId(job.IdempotencyKey));
    }

    [Fact]
    public void TheVersionsListIsAFirstPageOfTheBillSourceSoItSharesTheCongressApiOriginAndLimits()
    {
        var job = BillsSource().CreateFirstPageJob(ApiResource.BillTextVersions, Bill, new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero), Today);

        Assert.Equal((ApiResource.BillTextVersions, VersionsUrl, "https://api.congress.gov"),
            (job.Definition.ApiResource, job.Definition.Url, job.Definition.AllowedOrigin));
        Assert.StartsWith("bill-stream/congress-bills/text/119/hr/7/", job.IdempotencyKey);
    }

    [Fact]
    public void AJobIsBoundToItsOwnSourceAndResourceSoNeitherHostFetchesTheOthersUrls()
    {
        var text = TextSource();
        var bills = BillsSource();

        Assert.Throws<ArgumentException>(() => text.CreateApiJobDefinition(ApiResource.BillText, PackageUrl() + "?x=1", Today, CollectionJobPriority.Check));
        Assert.Throws<ArgumentException>(() => text.CreateApiJobDefinition(ApiResource.BillText, SourceUrl, Today, CollectionJobPriority.Check));
        Assert.Throws<ArgumentException>(() => text.CreateApiJobDefinition(ApiResource.BillTextVersions, VersionsUrl, Today, CollectionJobPriority.Check));
        Assert.Throws<ArgumentException>(() => bills.CreateApiJobDefinition(ApiResource.BillText, PackageUrl(), Today, CollectionJobPriority.Check));
        Assert.Throws<ArgumentException>(() => bills.CreateApiJobDefinition(ApiResource.BillTextVersions,
            VersionsUrl.Replace("limit=250", "limit=10"), Today, CollectionJobPriority.Check));
    }

    [Fact]
    public void TheTextSourceMakesNoScheduledRootJobBecauseTheDetailJobCreatesTheVersionsJob() =>
        Assert.Null(TextSource().CreateScheduledJobDefinition(DateTimeOffset.UtcNow, null));

    [Fact]
    public void TheTextSourceValidatesCoversNoPeopleAndStaysEligible()
    {
        var configuration = Configuration();
        var source = configuration.Sources.Last();

        configuration.Validate();
        Assert.Empty(configuration.CoveredPersonIdsOf(source, Today));
        Assert.Equal(source, configuration.EligibleSource(SourceId, Today));
    }

    [Fact]
    public void ACredentialIsRequiredBecauseGovinfoLimitsRequestsByKey() =>
        Assert.Throws<ArgumentException>(() => WithTextSource(Source() with { Credential = null }).Validate());

    [Fact]
    public void AValidatorIsRejectedBecauseA304WouldLeaveACheckWithoutACapture()
    {
        Assert.Throws<ArgumentException>(() => WithTextSource(Source() with { ETag = "\"v1\"" }).Validate());
        Assert.Throws<ArgumentException>(() => WithTextSource(Source() with { LastModified = DateTimeOffset.UnixEpoch }).Validate());
    }

    [Fact]
    public void CoverageAndCongressOptionsAreRejectedBecauseTheSourceCoversNoPeople()
    {
        Assert.Throws<ArgumentException>(() => WithTextSource(Source() with { MinimumCongress = 119 }).Validate());
        Assert.Throws<ArgumentException>(() => WithTextSource(Source() with { BackfillFrom = Today }).Validate());
    }

    [Fact]
    public void APolicyThatCannotHoldTheOneJobOfAVersionsReadIsRejectedSoTheAdmissionCannotFailLate()
    {
        var tooSmall = Source() with { AdmissionPolicy = new DiscoveryAdmissionPolicy { MaxJobs = 1, MaxTotalRequests = 1 } };

        Assert.Throws<ArgumentException>(() => WithTextSource(tooSmall).Validate());
    }

    [Fact]
    public void ASecondTextSourceIsRejectedBecauseTheVersionsReadFindsOneSource()
    {
        var second = Source() with { Id = "second", Url = SourceUrl + "/copy" };
        var bills = CongressBillSourceConfigurationTests.Configuration();

        Assert.Throws<ArgumentException>((bills with { Sources = [.. bills.Sources, Source(), second] }).Validate);
    }

    [Fact]
    public void TheShippedTextSourceIsOffByDefaultAndItsPolicyHoldsTheJobOfOneVersionsRead()
    {
        var shipped = JsonSerializer.Deserialize<CollectionConfiguration>(
            File.ReadAllText(Path.Combine(RepositoryRoot.FullName, "config", "federal-coverage.json")), ConfigurationJson.Options)!;
        var source = shipped.Sources.Single(candidate => candidate.ApiParser == ApiParser.CongressBillText);
        shipped.Validate();
        Assert.False(source.Enabled);

        var enabled = shipped with { Sources = [.. shipped.Sources.Select(item => item == source ? source with { Enabled = true } : item)] };
        var job = new ConfiguredCollectionSource(enabled, source.Id, Today).CreateBillTextJob("versions-job", PackageId, BillDay, Today);
        var batch = new ConfiguredCollectionSource(enabled, source.Id, Today).CreateApiBatch([job], Today);

        Assert.True(batch.Policy.CanReserve(batch.Template, 1));
        Assert.False(batch.Policy.CanReserve(batch.Template, 2));
        Assert.Equal("https://api.govinfo.gov", source.AllowedOrigin);
    }
}
