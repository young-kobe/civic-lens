using System.Text.Json;
using CivicLens.Application.Issues;
using CivicLens.Application.Publication;
using CivicLens.Core.Review;
using CivicLens.Host.Publication;
using CivicLens.Infrastructure.Actions;
using CivicLens.Infrastructure.Analysis;
using CivicLens.Infrastructure.Briefs;
using CivicLens.Infrastructure.ExecutiveActions;
using CivicLens.Infrastructure.Publication;
using CivicLens.Infrastructure.Relationships;
using CivicLens.Infrastructure.Review;
using CivicLens.Infrastructure.Statements;
using CivicLens.Infrastructure.Votes;
using CivicLens.Publication.Contracts;
using CivicLens.Tests.Application.Publication;
using CivicLens.Tests.Fixtures;
using CivicLens.Tests.Infrastructure.Collection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace CivicLens.Tests.Infrastructure.ExecutiveActions;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class PostgresExecutiveActionReviewStoreTests(PostgresCollection postgres) : IAsyncLifetime
{
    private readonly string captureRoot = Path.Combine(Path.GetTempPath(), "executive-action-review-" + Guid.NewGuid().ToString("N"));
    private string connectionString = null!;
    private PostgresExecutiveActionReviewStore store = null!;
    private ExecutiveActionSeeder executiveActions = null!;

    public async Task InitializeAsync()
    {
        connectionString = await postgres.CreateMigratedDatabaseAsync();
        store = PostgresExecutiveActionReviewStore.FromConnectionString(connectionString, TestCodeVersion.Current);
        executiveActions = new ExecutiveActionSeeder(connectionString, captureRoot);
    }

    public async Task DisposeAsync()
    {
        await postgres.DropDatabaseAsync(connectionString);
        Directory.Delete(captureRoot, recursive: true);
    }

    [Fact]
    public async Task ACodeApprovedRecordIsListedToPublishAndLoadsWithItsSavedEvidence()
    {
        var recordId = await executiveActions.AdmitAsync(FederalRegisterFixtures.TrumpOrder, "donald-trump");

        var listed = Assert.Single(await store.ListUnpublishedApprovedAsync(10, [], default));
        var publishable = (await store.GetForPublicationAsync([recordId], default))[recordId];

        Assert.Equal(recordId, listed.Action.RecordId);
        Assert.Equal(ReviewStatus.Approved, publishable.Review.CurrentStatus);
        Assert.Single(publishable.Extractions);
        Assert.Empty(await store.ListUnpublishedApprovedAsync(10, [recordId], default));
    }

    [Fact]
    public async Task AFailedApprovalCheckKeepsItsMessageAsTheHeldReasonUntilAPersonDecides()
    {
        var recordId = await executiveActions.AdmitAsync(FederalRegisterFixtures.TrumpOrder, "donald-trump", shiftedStoredTextPrefix: "Shifted. ");

        Assert.Equal("AwaitingReview", await ScalarAsync("SELECT review_status FROM executive_action_drafts"));
        Assert.Equal("An executive action fact must equal the text of its citation.", await HeldReasonAsync());
        var decision = new ReviewDecision(Guid.NewGuid().ToString("N"), recordId, 1, 1, ReviewDecisionKind.RequestChanges, "auth0|owner",
            "The quotes do not match.", [], DateTimeOffset.UtcNow);
        await store.DecideAsync("auth0|owner", decision, 1, 0, "key-held", new string('b', 64), [], default);

        Assert.Null(await HeldReasonAsync());
    }

    [Fact]
    public async Task TheOwnerCanWithdrawApprovalSoTheRecordLeavesTheReadyListAndNeedsAction()
    {
        var recordId = await executiveActions.AdmitAsync(FederalRegisterFixtures.TrumpOrder, "donald-trump");
        var decision = new ReviewDecision(Guid.NewGuid().ToString("N"), recordId, 1, 2, ReviewDecisionKind.WithdrawApproval, "auth0|owner",
            "The title is wrong.", [], DateTimeOffset.UtcNow);

        await store.DecideAsync("auth0|owner", decision, 1, 1, "key-1", new string('a', 64), [], default);

        Assert.Empty(await store.ListUnpublishedApprovedAsync(10, [], default));
        Assert.Equal(ReviewStatus.ApprovalWithdrawn, (await store.GetAsync(recordId, default))!.Review.CurrentStatus);
    }

    [Fact]
    public async Task ACorrectionAfterAWithdrawalWaitsForAPersonBecauseTheOpenConcernBlocksCodeApproval()
    {
        var recordId = await executiveActions.AdmitAsync(FederalRegisterFixtures.BidenProclamation, "joe-biden");
        var decision = new ReviewDecision(Guid.NewGuid().ToString("N"), recordId, 1, 2, ReviewDecisionKind.WithdrawApproval, "auth0|owner",
            "Check the date.", [], DateTimeOffset.UtcNow);
        await store.DecideAsync("auth0|owner", decision, 1, 1, "key-1", new string('a', 64), [], default);

        await executiveActions.AdmitAsync(FederalRegisterFixtures.BidenProclamationCorrection, "joe-biden");

        Assert.Empty(await store.ListUnpublishedApprovedAsync(10, [], default));
        var review = (await store.GetAsync(recordId, default))!.Review;
        Assert.Equal(2, review.CurrentRevision.RevisionNumber);
        Assert.Single(review.UnresolvedConcerns);
    }

    [Fact]
    public async Task ACorrectionOfARejectedRecordWaitsAndDoesNotReopenItBecauseARejectionStays()
    {
        var recordId = await executiveActions.AdmitAsync(FederalRegisterFixtures.BidenProclamation, "joe-biden");
        var withdrawal = new ReviewDecision(Guid.NewGuid().ToString("N"), recordId, 1, 2, ReviewDecisionKind.WithdrawApproval, "auth0|owner",
            "Check the date.", [], DateTimeOffset.UtcNow);
        await store.DecideAsync("auth0|owner", withdrawal, 1, 1, "key-1", new string('a', 64), [], default);
        var rejection = new ReviewDecision(Guid.NewGuid().ToString("N"), recordId, 1, 3, ReviewDecisionKind.Reject, "auth0|owner",
            null, [], DateTimeOffset.UtcNow, ReasonCode: ReviewReasonCode.Duplicate);
        await store.DecideAsync("auth0|owner", rejection, 1, 2, "key-2", new string('b', 64), [], default);

        await executiveActions.AdmitAsync(FederalRegisterFixtures.BidenProclamationCorrection, "joe-biden");

        var review = (await store.GetAsync(recordId, default))!.Review;
        Assert.Equal(1, review.CurrentRevision.RevisionNumber);
        Assert.Equal(ReviewStatus.Rejected, review.CurrentStatus);
    }

    [Fact]
    public async Task AnAdmittedRevisionStoresTheBuildShaBecauseTheCodeThatWroteTheFactsMustBeTraceable()
    {
        var recordId = await executiveActions.AdmitAsync(FederalRegisterFixtures.TrumpOrder, "donald-trump");

        Assert.Equal(TestCodeVersion.Sha, await ScalarAsync($"SELECT code_version FROM executive_action_revisions WHERE record_id = '{recordId}' AND revision_number = 1"));
    }

    [Fact]
    public async Task ARevisionAppendedByAPersonApprovalStoresTheBuildShaBecauseCodeWritesItAndNotThePerson()
    {
        var recordId = await RejectedRecordWithTwoWaitingCorrectionsAsync();
        var changes = await DecideAsync(recordId, 4, ReviewDecisionKind.RequestChanges, "Look again.");

        await DecideAsync(recordId, 5, ReviewDecisionKind.Approve, "Looked again.", [(await ConcernIdsAsync(recordId)).Single(id => id != changes.DecisionId), changes.DecisionId]);

        Assert.Equal(TestCodeVersion.Sha, await ScalarAsync($"SELECT code_version FROM executive_action_revisions WHERE record_id = '{recordId}' AND revision_number = 2"));
    }

    [Fact]
    public async Task ReversingARejectionAppliesTheCorrectionThatWaitedAndCodeApprovesTheNewRevisionBecauseOldFactsMustNotPublish()
    {
        var recordId = await executiveActions.AdmitAsync(FederalRegisterFixtures.BidenProclamation, "joe-biden");
        var withdrawal = new ReviewDecision(Guid.NewGuid().ToString("N"), recordId, 1, 2, ReviewDecisionKind.WithdrawApproval, "auth0|owner",
            "Check the date.", [], DateTimeOffset.UtcNow);
        await store.DecideAsync("auth0|owner", withdrawal, 1, 1, "key-1", new string('a', 64), [], default);
        var rejection = new ReviewDecision(Guid.NewGuid().ToString("N"), recordId, 1, 3, ReviewDecisionKind.Reject, "auth0|owner",
            null, [], DateTimeOffset.UtcNow, ReasonCode: ReviewReasonCode.Duplicate);
        await store.DecideAsync("auth0|owner", rejection, 1, 2, "key-2", new string('b', 64), [], default);
        await executiveActions.AdmitAsync(FederalRegisterFixtures.BidenProclamationCorrection, "joe-biden");
        var reversal = new ReviewDecision(Guid.NewGuid().ToString("N"), recordId, 1, 4, ReviewDecisionKind.Approve, "auth0|owner",
            "Rejected by mistake.", [withdrawal.DecisionId], DateTimeOffset.UtcNow, "Clarified the date.");

        await store.DecideAsync("auth0|owner", reversal, 1, 3, "key-3", new string('c', 64), [], default);

        var review = (await store.GetAsync(recordId, default))!.Review;
        Assert.Equal(2, review.CurrentRevision.RevisionNumber);
        Assert.Equal(ReviewStatus.Approved, review.CurrentStatus);
        Assert.Equal(ReviewAuthor.CodeSubject, review.Decisions[^1].ActorSubject);
        Assert.Single(review.CurrentRevision.Notices);
        Assert.Equal("Clarified the date.", review.Decisions[^1].CorrectionNote);
    }

    [Fact]
    public async Task ApprovingAfterRequestChangesAppliesTheWaitingCorrectionsInOneRevisionBecauseNoPersonDecisionMaySkipThem()
    {
        var recordId = await RejectedRecordWithTwoWaitingCorrectionsAsync();
        var changes = await DecideAsync(recordId, 4, ReviewDecisionKind.RequestChanges, "Look again.");

        await DecideAsync(recordId, 5, ReviewDecisionKind.Approve, "Looked again.", [(await ConcernIdsAsync(recordId)).Single(id => id != changes.DecisionId), changes.DecisionId]);

        var review = (await store.GetAsync(recordId, default))!.Review;
        Assert.Equal(2, review.CurrentRevision.RevisionNumber);
        Assert.Equal(2, review.CurrentRevision.Notices.Length);
        Assert.Equal(ReviewStatus.Approved, review.CurrentStatus);
    }

    [Fact]
    public async Task ReversingARejectionAppliesTwoWaitingCorrectionsInOneRevisionBecauseTheOriginalAdmissionDoesTheSame()
    {
        var recordId = await RejectedRecordWithTwoWaitingCorrectionsAsync();

        await DecideAsync(recordId, 4, ReviewDecisionKind.Approve, "Rejected by mistake.", await ConcernIdsAsync(recordId));

        var review = (await store.GetAsync(recordId, default))!.Review;
        Assert.Equal(2, review.CurrentRevision.RevisionNumber);
        Assert.Equal(2, review.CurrentRevision.Notices.Length);
    }

    [Fact]
    public async Task ACorrectionThatArrivesAfterRequestChangesCarriesTheCorrectionsThatWaitedBecauseNoneMayBeLost()
    {
        var recordId = await RejectedRecordWithTwoWaitingCorrectionsAsync(admitSecond: false);
        await DecideAsync(recordId, 4, ReviewDecisionKind.RequestChanges, "Look again.");

        await executiveActions.AdmitAsync(SecondCorrection, "joe-biden");

        var review = (await store.GetAsync(recordId, default))!.Review;
        Assert.Equal(2, review.CurrentRevision.RevisionNumber);
        Assert.Equal(2, review.CurrentRevision.Notices.Length);
    }

    private static string SecondCorrection => FederalRegisterFixtures.BidenProclamationCorrection.Replace("R1-2024-26714", "R2-2024-26714", StringComparison.Ordinal);

    private async Task<string> RejectedRecordWithTwoWaitingCorrectionsAsync(bool admitSecond = true)
    {
        var recordId = await executiveActions.AdmitAsync(FederalRegisterFixtures.BidenProclamation, "joe-biden");
        await DecideAsync(recordId, 2, ReviewDecisionKind.WithdrawApproval, "Check the date.");
        await DecideAsync(recordId, 3, ReviewDecisionKind.Reject, null, reason: ReviewReasonCode.Duplicate);
        await executiveActions.AdmitAsync(FederalRegisterFixtures.BidenProclamationCorrection, "joe-biden");
        if (admitSecond) await executiveActions.AdmitAsync(SecondCorrection, "joe-biden");
        return recordId;
    }

    private async Task<string[]> ConcernIdsAsync(string recordId) =>
        [.. (await store.GetAsync(recordId, default))!.Review.UnresolvedConcerns.Select(concern => concern.DecisionId)];

    private async Task<ReviewDecision> DecideAsync(string recordId, int version, ReviewDecisionKind kind, string? note, string[]? resolved = null,
        ReviewReasonCode? reason = null)
    {
        var decision = new ReviewDecision(Guid.NewGuid().ToString("N"), recordId, 1, version, kind, "auth0|owner", note, [.. resolved ?? []],
            DateTimeOffset.UtcNow, ReasonCode: reason);
        return await store.DecideAsync("auth0|owner", decision, 1, version - 1, Guid.NewGuid().ToString("N"), new string('a', 64), [], default);
    }

    [Fact]
    public async Task ApprovingTheNewRevisionClosesTheListedOlderReportInTheSameTransactionBecauseAPersonHasNoOtherWayToEditThisRecord()
    {
        var recordId = await executiveActions.AdmitAsync(FederalRegisterFixtures.BidenProclamation, "joe-biden");
        var withdrawal = new ReviewDecision(Guid.NewGuid().ToString("N"), recordId, 1, 2, ReviewDecisionKind.WithdrawApproval, "auth0|owner",
            "Check the date.", [], DateTimeOffset.UtcNow);
        await store.DecideAsync("auth0|owner", withdrawal, 1, 1, "key-1", new string('a', 64), [], default);
        await executiveActions.AdmitAsync(FederalRegisterFixtures.BidenProclamationCorrection, "joe-biden");
        var review = (await store.GetAsync(recordId, default))!.Review;
        var reportId = Guid.NewGuid();
        await ExecuteAsync($"""
            INSERT INTO reader_reports (report_id, record_id, kind, revision_number, category, body, received_at_utc_ticks, status, version)
            VALUES ('{reportId}', '{recordId}', 'ExecutiveAction', 1, 'other', 'text', 1, 'Open', 0)
            """);
        var approval = new ReviewDecision(Guid.NewGuid().ToString("N"), recordId, 2, review.ReviewStateVersion + 1, ReviewDecisionKind.Approve,
            "auth0|owner", "Checked.", [review.UnresolvedConcerns.Single().DecisionId], DateTimeOffset.UtcNow, "The date is fixed.");

        await store.DecideAsync("auth0|owner", approval, 2, review.ReviewStateVersion, "key-2", new string('c', 64), [reportId], default);

        Assert.Equal("CorrectionNeeded", await ScalarAsync($"SELECT status FROM reader_reports WHERE report_id = '{reportId}'"));
    }

    [Fact]
    public async Task ARequestForChangesClosesNothingAndAnApprovalClosesOnlyAListedReportOfItsOwnKind()
    {
        var recordId = await executiveActions.AdmitAsync(FederalRegisterFixtures.BidenProclamation, "joe-biden");
        var withdrawal = new ReviewDecision(Guid.NewGuid().ToString("N"), recordId, 1, 2, ReviewDecisionKind.WithdrawApproval, "auth0|owner",
            "Check the date.", [], DateTimeOffset.UtcNow);
        await store.DecideAsync("auth0|owner", withdrawal, 1, 1, "key-1", new string('a', 64), [], default);
        await executiveActions.AdmitAsync(FederalRegisterFixtures.BidenProclamationCorrection, "joe-biden");
        var review = (await store.GetAsync(recordId, default))!.Review;
        var onThisRecord = Guid.NewGuid();
        var ofAnotherKind = Guid.NewGuid();
        foreach (var (id, kind) in new[] { (onThisRecord, "ExecutiveAction"), (ofAnotherKind, "Statement") })
            await ExecuteAsync($"""
                INSERT INTO reader_reports (report_id, record_id, kind, revision_number, category, body, received_at_utc_ticks, status, version)
                VALUES ('{id}', '{recordId}', '{kind}', 1, 'other', 'text', 1, 'Open', 0)
                """);
        var requestChanges = new ReviewDecision(Guid.NewGuid().ToString("N"), recordId, 2, review.ReviewStateVersion + 1,
            ReviewDecisionKind.RequestChanges, "auth0|owner", "Still wrong.", [], DateTimeOffset.UtcNow);

        await store.DecideAsync("auth0|owner", requestChanges, 2, review.ReviewStateVersion, "key-2", new string('d', 64), [onThisRecord, ofAnotherKind], default);

        Assert.Equal(("Open", "Open"), (await ScalarAsync($"SELECT status FROM reader_reports WHERE report_id = '{onThisRecord}'"),
            await ScalarAsync($"SELECT status FROM reader_reports WHERE report_id = '{ofAnotherKind}'")));
        var after = (await store.GetAsync(recordId, default))!.Review;
        var approval = new ReviewDecision(Guid.NewGuid().ToString("N"), recordId, 2, after.ReviewStateVersion + 1, ReviewDecisionKind.Approve,
            "auth0|owner", "Checked.", [.. after.UnresolvedConcerns.Select(concern => concern.DecisionId)], DateTimeOffset.UtcNow, "The date is fixed.");

        await store.DecideAsync("auth0|owner", approval, 2, after.ReviewStateVersion, "key-3", new string('e', 64), [onThisRecord, ofAnotherKind], default);

        Assert.Equal(("CorrectionNeeded", "Open"), (await ScalarAsync($"SELECT status FROM reader_reports WHERE report_id = '{onThisRecord}'"),
            await ScalarAsync($"SELECT status FROM reader_reports WHERE report_id = '{ofAnotherKind}'")));
    }

    [Fact]
    public async Task ARecordPublishesWithItsRealPageThenLeavesTheReadyListAndWithdrawsFromTheSite()
    {
        var recordId = await executiveActions.AdmitAsync(FederalRegisterFixtures.TrumpOrder, "donald-trump");
        var releases = new FileReleaseDirectory(Path.Combine(captureRoot, "releases"));
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var publications = PostgresPublicationStore.FromConnectionString(connectionString, TestCodeVersion.Current);
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
        Assert.Empty(await store.ListUnpublishedApprovedAsync(10, [], default));

        var decision = new ReviewDecision(Guid.NewGuid().ToString("N"), recordId, 1, 2, ReviewDecisionKind.WithdrawApproval, "auth0|owner",
            "The title is wrong.", [], DateTimeOffset.UtcNow);
        await store.DecideAsync("auth0|owner", decision, 1, 1, "key-1", new string('a', 64), [], default);
        clock.Now = clock.Now.AddMinutes(1);
        var withdrawn = await publisher.ExecuteAsync(PublicationScenario.Owner,
            new PublishReleaseRequest([], "withdraw") { Withdrawals = [new(PublishedRecordKind.ExecutiveAction, recordId, "The title is wrong.")] });
        Assert.Empty(JsonSerializer.Deserialize<PublicationRelease>(await releases.ReadFileAsync(withdrawn.DirectoryName,
            PublicationProtocol.ManifestPath, PublicationProtocol.MaximumManifestBytes, default), PublicationProtocol.JsonOptions)!.Records);
    }

    [Fact]
    public async Task TheReadyListShowsAReplacementFirstThenTheOldestBecauseACorrectionMustReachReadersSoonest()
    {
        await executiveActions.AdmitAsync(FederalRegisterFixtures.TrumpOrder, "donald-trump");
        await executiveActions.AdmitAsync(FederalRegisterFixtures.BidenProclamation, "joe-biden");
        await executiveActions.AdmitAsync(FederalRegisterFixtures.BidenNotice, "joe-biden");
        var ids = (await ScalarAsync("SELECT string_agg(record_id, ',' ORDER BY record_id) FROM executive_action_drafts"))!.Split(',');
        var (replacement, oldest, newer) = (ids[0], ids[1], ids[2]);
        await ExecuteAsync($"""
            UPDATE executive_action_drafts SET created_at_utc_ticks = CASE record_id
                WHEN '{oldest}' THEN 10 WHEN '{newer}' THEN 20 ELSE 30 END;
            INSERT INTO publication_releases (release_number, directory_name, actor_subject, published_at_utc_ticks, record_count, is_active)
            VALUES (1, '000001-' || md5('order'), 'owner', 0, 1, true);
            INSERT INTO publication_record_states (kind, record_id, revision_number, first_release)
            VALUES ('executiveAction', '{replacement}', 0, 1);
            """);

        var listed = await store.ListUnpublishedApprovedAsync(10, [], default);

        Assert.Equal([replacement, oldest, newer], listed.Select(item => item.Action.RecordId));
    }

    [Fact]
    public async Task ReplacementsTakeAtMostHalfTheBatchWhileNewRecordsWaitBecauseCorrectionsMustNotHoldBackNewRecords()
    {
        await executiveActions.AdmitAsync(FederalRegisterFixtures.TrumpOrder, "donald-trump");
        await executiveActions.AdmitAsync(FederalRegisterFixtures.BidenProclamation, "joe-biden");
        await executiveActions.AdmitAsync(FederalRegisterFixtures.BidenNotice, "joe-biden");
        var ids = (await ScalarAsync("SELECT string_agg(record_id, ',' ORDER BY record_id) FROM executive_action_drafts"))!.Split(',');
        await ExecuteAsync($"""
            UPDATE executive_action_drafts SET created_at_utc_ticks = CASE record_id WHEN '{ids[0]}' THEN 10 WHEN '{ids[1]}' THEN 20 ELSE 30 END;
            INSERT INTO publication_releases (release_number, directory_name, actor_subject, published_at_utc_ticks, record_count, is_active)
            VALUES (1, '000001-' || md5('half'), 'owner', 0, 2, true);
            INSERT INTO publication_record_states (kind, record_id, revision_number, first_release)
            VALUES ('executiveAction', '{ids[0]}', 0, 1), ('executiveAction', '{ids[1]}', 0, 1);
            """);

        var batch = await store.ListUnpublishedApprovedAsync(2, [], default);
        var replacementsOnly = await store.ListUnpublishedApprovedAsync(2, [ids[2]], default);

        Assert.Equal([ids[0], ids[2]], batch.Select(item => item.Action.RecordId));
        Assert.Equal([ids[0], ids[1]], replacementsOnly.Select(item => item.Action.RecordId));
        Assert.Equal(1, await PostgresPublicationStore.FromConnectionString(connectionString, TestCodeVersion.Current).CountApprovedOutsideActiveReleaseAsync([], default));
    }

    [Theory]
    [InlineData(1, new[] { 3 })]
    [InlineData(4, new[] { 0, 3, 1, 4 })]
    [InlineData(5, new[] { 0, 3, 1, 4, 5 })]
    [InlineData(6, new[] { 0, 3, 1, 4, 2, 5 })]
    public async Task ReplacementsAlternateWithNewRecordsUpToHalfTheLimitSoACutFromTheFrontKeepsTheBound(int limit, int[] expected)
    {
        var ids = await AdmitSixAsync();
        await ExecuteAsync($"""
            UPDATE executive_action_drafts SET created_at_utc_ticks = CASE record_id {string.Join(" ", ids.Select((id, index) => $"WHEN '{id}' THEN {(index + 1) * 10}"))} END;
            INSERT INTO publication_releases (release_number, directory_name, actor_subject, published_at_utc_ticks, record_count, is_active)
            VALUES (1, '000001-' || md5('alternate'), 'owner', 0, 3, true);
            INSERT INTO publication_record_states (kind, record_id, revision_number, first_release)
            VALUES {string.Join(", ", ids.Take(3).Select(id => $"('executiveAction', '{id}', 0, 1)"))};
            """);

        var batch = await store.ListUnpublishedApprovedAsync(limit, [], default);

        Assert.Equal(expected.Select(index => ids[index]), batch.Select(item => item.Action.RecordId));
    }

    private async Task<string[]> AdmitSixAsync()
    {
        foreach (var number in Enumerable.Range(1, 6))
            await executiveActions.AdmitAsync(FederalRegisterFixtures.TrumpOrder.Replace("2025-01900", $"2025-0190{number}"), "donald-trump");
        return (await ScalarAsync("SELECT string_agg(record_id, ',' ORDER BY record_id) FROM executive_action_drafts"))!.Split(',');
    }

    private Task<string?> HeldReasonAsync() => ScalarAsync("SELECT held_reason FROM executive_action_drafts");

    private async Task ExecuteAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<string?> ScalarAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return await command.ExecuteScalarAsync() as string;
    }
}
