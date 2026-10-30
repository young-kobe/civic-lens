using System.Text.Json;
using CivicLens.Application.Collection;
using CivicLens.Application.Collection.FederalRegister;
using CivicLens.Application.Collection.Jobs;
using CivicLens.Application.Configuration;
using CivicLens.Collection.Contracts;
using CivicLens.Tests.Fixtures;

namespace CivicLens.Tests.Application.Collection.FederalRegister;

public sealed class FederalRegisterSourceConfigurationTests
{
    private static readonly DateOnly Today = new(2026, 10, 29);

    [Fact]
    public void TheSourceCoversNoPeopleAndStaysEligibleSoTheSixHourCheckRuns()
    {
        var configuration = FederalRegisterFixtures.Configuration();
        var source = configuration.Sources.Single();

        Assert.Empty(configuration.CoveredPersonIdsOf(source, Today));
        Assert.Empty(configuration.ListedPersonIdsOf(source));
        Assert.True(configuration.IsActiveOn(source, Today));
    }

    [Fact]
    public void ACredentialAValidatorACoverageListOrACongressOptionIsRefused()
    {
        var configuration = FederalRegisterFixtures.Configuration();
        var source = configuration.Sources.Single();
        WatchedSourceConfiguration[] changes =
        [
            source with { Credential = CollectionCredentials.CongressGov },
            source with { ETag = "\"x\"" },
            source with { MinimumCongress = 119 },
            source with { BackfillFrom = null },
            source with { Url = "https://www.federalregister.gov/api/v1/documents" }
        ];

        Assert.All(changes, changed => Assert.Throws<ArgumentException>((configuration with { Sources = [changed] }).Validate));
    }

    [Fact]
    public void ASecondFederalRegisterSourceIsRefusedBecauseOneStreamHoldsTheNewestDate()
    {
        var configuration = FederalRegisterFixtures.Configuration();
        var source = configuration.Sources.Single();

        Assert.Throws<ArgumentException>((configuration with
        {
            Sources = [source, source with { Id = "second", Url = "https://www.federalregister.gov/api/v1/documents/other.json" }]
        }).Validate);
    }

    [Fact]
    public void APolicyThatCannotHoldAFullListOfDocumentsIsRefusedSoNoAdmissionDefersAJob()
    {
        var configuration = FederalRegisterFixtures.Configuration();
        var source = configuration.Sources.Single();
        var policy = source.AdmissionPolicy! with { MaxJobs = FederalRegisterUrl.PageLimit - 1 };

        Assert.Throws<ArgumentException>((configuration with { Sources = [source with { AdmissionPolicy = policy }] }).Validate);
    }

    [Fact]
    public void TheScheduleRequestsTheWindowFromTheBackfillDateWhenNoDocumentIsKnownAndNeverAJobOnRequest()
    {
        var source = new ConfiguredCollectionSource(FederalRegisterFixtures.Configuration(), FederalRegisterFixtures.SourceId, Today);

        var definition = source.CreateScheduledJobDefinition(new DateTimeOffset(2026, 10, 29, 3, 0, 0, TimeSpan.Zero), null)!;

        Assert.True(source.IsCollectedBySchedule);
        Assert.Equal(ApiResource.FederalRegisterList, definition.ApiResource);
        Assert.Equal(FederalRegisterUrl.List(FederalRegisterFixtures.SourceUrl, new FederalRegisterWindow(FederalRegisterFixtures.Backfill, Today)), definition.Url);
        Assert.Null(definition.Credential);
        Assert.Throws<ArgumentException>(() => source.CreateJobDefinition(Today));
    }

    [Fact]
    public void AJobWhoseUrlHasNotTheFormOfItsResourceIsRefused()
    {
        var source = new ConfiguredCollectionSource(FederalRegisterFixtures.Configuration(), FederalRegisterFixtures.SourceId, Today);

        Assert.Throws<ArgumentException>(() => source.CreateApiJobDefinition(ApiResource.FederalRegisterDocument,
            FederalRegisterFixtures.SourceUrl + "?fields%5B%5D=document_number", Today, CollectionJobPriority.Check));
        _ = source.CreateApiJobDefinition(ApiResource.FederalRegisterDocument, FederalRegisterUrl.Document(FederalRegisterFixtures.SourceUrl, "2025-01900"),
            Today, CollectionJobPriority.Check);
    }

    [Fact]
    public void TheListBatchHoldsOneDocumentJobForEachItemWithTheDayPriorityAndTheShippedPolicyHoldsAFullList()
    {
        var shipped = JsonSerializer.Deserialize<CollectionConfiguration>(
            File.ReadAllText(Path.Combine(RepositoryRoot.FullName, "config", "federal-coverage.json")), ConfigurationJson.Options)!;
        var source = shipped.Sources.Single(candidate => candidate.ApiParser == ApiParser.FederalRegisterDocuments);
        shipped.Validate();
        Assert.False(source.Enabled);
        Assert.Equal(21_600, source.CheckIntervalSeconds);
        Assert.Equal(new DateOnly(2024, 10, 29), source.BackfillFrom);

        var enabled = shipped with { Sources = [.. shipped.Sources.Select(item => item == source ? source with { Enabled = true } : item)] };
        var page = FederalRegisterListParser.Parse(FederalRegisterFixtures.Bytes(FederalRegisterFixtures.List));
        var mixed = new FederalRegisterListPage([page.Items[0] with { PublishedOn = Today }, page.Items[1] with { PublishedOn = Today.AddDays(-30) }]);
        var batch = new ConfiguredCollectionSource(enabled, source.Id, Today).CreateFederalRegisterListBatch(mixed, Today);

        Assert.Equal(["federal-register/federal-register-documents/document/2025-01900", "federal-register/federal-register-documents/document/2025-01901"],
            batch.Jobs.Select(job => job.IdempotencyKey));
        Assert.Equal([CollectionJobPriority.Check, CollectionJobPriority.Backfill], batch.Jobs.Select(job => job.Definition.Priority));
        Assert.True(batch.Policy.CanReserve(batch.Template, FederalRegisterUrl.PageLimit));
    }
}
