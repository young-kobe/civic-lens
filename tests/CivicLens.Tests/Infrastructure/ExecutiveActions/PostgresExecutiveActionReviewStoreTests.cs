using CivicLens.Tests.Infrastructure.Collection;
using System.Collections.Immutable;
using CivicLens.Application.Collection.FederalRegister;
using CivicLens.Application.Collection;
using CivicLens.Core.Review;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Collection.Jobs;
using CivicLens.Infrastructure.Documents;
using CivicLens.Infrastructure.ExecutiveActions;
using CivicLens.Tests.Fixtures;
using CivicLens.Application.Issues;
using CivicLens.Application.Publication;
using CivicLens.Host.Publication;
using CivicLens.Infrastructure.Actions;
using CivicLens.Infrastructure.Analysis;
using CivicLens.Infrastructure.Briefs;
using CivicLens.Infrastructure.Publication;
using CivicLens.Infrastructure.Relationships;
using CivicLens.Infrastructure.Review;
using CivicLens.Infrastructure.Statements;
using CivicLens.Infrastructure.Votes;
using CivicLens.Publication.Contracts;
using CivicLens.Tests.Application.Publication;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;
using Npgsql;

namespace CivicLens.Tests.Infrastructure.ExecutiveActions;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class PostgresExecutiveActionReviewStoreTests(PostgresCollection postgres) : IAsyncLifetime
{
    private readonly string captureRoot = Path.Combine(Path.GetTempPath(), "executive-action-review-" + Guid.NewGuid().ToString("N"));
    private string connectionString = null!;
    private PostgresCollectionJobStore jobs = null!;
    private PostgresExecutiveActionReviewStore store = null!;
    private JobExtractionSeeder extractions = null!;

    public async Task InitializeAsync()
    {
        connectionString = await postgres.CreateMigratedDatabaseAsync();
        jobs = PostgresCollectionJobStore.FromConnectionString(connectionString);
        store = PostgresExecutiveActionReviewStore.FromConnectionString(connectionString);
        var documents = PostgresDocumentExtractionStore.FromConnectionString(connectionString);
        Directory.CreateDirectory(captureRoot);
        var seeder = new StatementCandidateSeeder(PostgresCollectionAttemptStore.FromConnectionString(connectionString), documents,
            connectionString, captureRoot);
        extractions = new JobExtractionSeeder(jobs, documents, seeder, connectionString, captureRoot);
    }

    public async Task DisposeAsync()
    {
        await postgres.DropDatabaseAsync(connectionString);
        Directory.Delete(captureRoot, recursive: true);
    }

    [Fact]
    public async Task ACodeApprovedRecordIsListedForCodeToPublishAndLoadsWithItsSavedEvidence()
    {
        var recordId = await AdmitAsync(FederalRegisterFixtures.TrumpOrder, "donald-trump");

        var listed = Assert.Single(await store.ListCodeApprovedUnpublishedAsync(10, [], default));
        var publishable = (await store.GetForPublicationAsync([recordId], default))[recordId];

        Assert.Equal(recordId, listed.Action.RecordId);
        Assert.Null(listed.PublishedRevisionNumber);
        Assert.Equal(ReviewStatus.Approved, publishable.Review.CurrentStatus);
        Assert.Single(publishable.Extractions);
        Assert.Empty(await store.ListCodeApprovedUnpublishedAsync(10, [recordId], default));
    }

    [Fact]
    public async Task TheOwnerCanWithdrawApprovalSoTheRecordLeavesTheReadyListAndNeedsAction()
    {
        var recordId = await AdmitAsync(FederalRegisterFixtures.TrumpOrder, "donald-trump");
        var decision = new ReviewDecision(Guid.NewGuid().ToString("N"), recordId, 1, 2, ReviewDecisionKind.WithdrawApproval, "auth0|owner",
            "The title is wrong.", [], DateTimeOffset.UtcNow);

        await store.DecideAsync("auth0|owner", decision, 1, 1, "key-1", new string('a', 64), default);

        Assert.Empty(await store.ListCodeApprovedUnpublishedAsync(10, [], default));
        var needing = await store.ListNeedingActionAsync(10, default);
        Assert.Equal(1, needing.Total);
        Assert.Equal(ReviewStatus.ApprovalWithdrawn, Assert.Single(needing.Items).CurrentStatus);
        Assert.Equal(ReviewStatus.ApprovalWithdrawn, (await store.GetAsync(recordId, default))!.Review.CurrentStatus);
    }

    [Fact]
    public async Task ACorrectionAfterAWithdrawalWaitsForAPersonBecauseTheOpenConcernBlocksCodeApproval()
    {
        var recordId = await AdmitAsync(FederalRegisterFixtures.BidenProclamation, "joe-biden");
        var decision = new ReviewDecision(Guid.NewGuid().ToString("N"), recordId, 1, 2, ReviewDecisionKind.WithdrawApproval, "auth0|owner",
            "Check the date.", [], DateTimeOffset.UtcNow);
        await store.DecideAsync("auth0|owner", decision, 1, 1, "key-1", new string('a', 64), default);

        await AdmitAsync(FederalRegisterFixtures.BidenProclamationCorrection, "joe-biden");

        Assert.Empty(await store.ListCodeApprovedUnpublishedAsync(10, [], default));
        var needing = Assert.Single((await store.ListNeedingActionAsync(10, default)).Items);
        Assert.Equal(2, needing.CurrentRevision.RevisionNumber);
        Assert.Single(needing.UnresolvedConcerns);
    }

    [Fact]
    public async Task ARecordPublishesWithItsRealPageThenLeavesTheReadyListAndWithdrawsFromTheSite()
    {
        var recordId = await AdmitAsync(FederalRegisterFixtures.TrumpOrder, "donald-trump");
        var releases = new FileReleaseDirectory(Path.Combine(captureRoot, "releases"));
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var publications = PostgresPublicationStore.FromConnectionString(connectionString);
        var publisher = new PublishRelease(publications, releases, new ReleaseRenderer(null, null), new PublishedRecordLoaders(
                PostgresDocumentChangeReviewStore.FromConnectionString(connectionString), PostgresStatementReviewStore.FromConnectionString(connectionString),
                PostgresActionReviewStore.FromConnectionString(connectionString), PostgresVoteReviewStore.FromConnectionString(connectionString),
                new PostgresRelationshipReviewStore(AnalysisDbContextFactory.Create(connectionString)),
                new PostgresBriefReviewStore(AnalysisDbContextFactory.Create(connectionString)), store),
            new PublicationCatalog(new Dictionary<string, string> { ["donald-trump"] = "Donald Trump" }, IssueConfiguration.None),
            FixedRosters.NamedOnly(["donald-trump"]), PhotoCaptures.None(), clock, new PruneReleases(publications, releases, NullLogger<PruneReleases>.Instance));
        var reference = new PublishedRecordReference(PublishedRecordKind.ExecutiveAction, recordId);

        var summary = await publisher.ExecuteAsync(new ReviewActor(ReviewAuthor.CodeSubject, ReviewRole.Owner), PublicationScenario.Request("publish", reference));

        var page = System.Text.Encoding.UTF8.GetString(await releases.ReadFileAsync(summary.DirectoryName, PublicationProtocol.RecordPagePath(recordId),
            PublicationProtocol.MaximumManifestBytes, default));
        Assert.Contains("Ending the Weaponization of the Federal Government", page);
        Assert.Contains("title from the Federal Register", page);
        Assert.Contains("https://www.federalregister.gov/documents/2025/01/28/2025-01900/", page);
        Assert.Empty(await store.ListCodeApprovedUnpublishedAsync(10, [], default));

        var decision = new ReviewDecision(Guid.NewGuid().ToString("N"), recordId, 1, 2, ReviewDecisionKind.WithdrawApproval, "auth0|owner",
            "The title is wrong.", [], DateTimeOffset.UtcNow);
        await store.DecideAsync("auth0|owner", decision, 1, 1, "key-1", new string('a', 64), default);
        clock.Now = clock.Now.AddMinutes(1);
        var withdrawn = await publisher.ExecuteAsync(PublicationScenario.Owner,
            new PublishReleaseRequest([], "withdraw") { Withdrawals = [new(PublishedRecordKind.ExecutiveAction, recordId, "The title is wrong.")] });
        Assert.Empty(JsonSerializer.Deserialize<PublicationRelease>(await releases.ReadFileAsync(withdrawn.DirectoryName,
            PublicationProtocol.ManifestPath, PublicationProtocol.MaximumManifestBytes, default), PublicationProtocol.JsonOptions)!.Records);
    }

    private async Task<string> AdmitAsync(string json, string personId)
    {
        var document = FederalRegisterFixtures.Document(json);
        var source = new ConfiguredCollectionSource(FederalRegisterFixtures.Configuration(), FederalRegisterFixtures.SourceId,
            DateOnly.FromDateTime(DateTime.UtcNow));
        var page = new FederalRegisterListPage([new FederalRegisterListItem(document.DocumentNumber, document.PublishedOn)]);
        var jobId = Assert.Single((await jobs.AdmitListPageAsync(source, page, default)).Jobs).JobId;
        await SucceededJobs.MarkSucceededAsync(connectionString, jobId);
        var text = FederalRegisterRenderer.Render(document);
        var extractionId = await extractions.SaveAsync(jobId, json, text, FederalRegisterDocumentParser.ExtractionVersion);
        await jobs.AdmitDocumentAsync(source, jobId, new FederalRegisterEvidence(document, extractionId, text, personId), default);
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT record_id FROM executive_action_drafts LIMIT 1", connection);
        return (string)(await command.ExecuteScalarAsync())!;
    }
}
