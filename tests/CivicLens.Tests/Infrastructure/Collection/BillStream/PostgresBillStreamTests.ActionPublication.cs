using CivicLens.Infrastructure.ExecutiveActions;
using Microsoft.Extensions.Logging.Abstractions;
using CivicLens.Tests.Fixtures;
using CivicLens.Application.Issues;
using CivicLens.Infrastructure.Relationships;
using CivicLens.Infrastructure.Votes;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using CivicLens.Application.Collection.Bills;
using CivicLens.Application.Publication;
using CivicLens.Core.Actions;
using CivicLens.Core.Review;
using CivicLens.Host.Publication;
using CivicLens.Infrastructure.Analysis;
using CivicLens.Infrastructure.Briefs;
using CivicLens.Infrastructure.Publication;
using CivicLens.Infrastructure.Review;
using CivicLens.Infrastructure.Statements;
using CivicLens.Publication.Contracts;
using CivicLens.Tests.Application.Publication;

namespace CivicLens.Tests.Infrastructure.Collection.BillStream;

public sealed partial class PostgresBillStreamTests
{
    private static readonly DateTimeOffset ReleasedAt = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task AnActionDecisionAfterTheBuildBlocksTheCommitBecauseApprovalMayHaveChanged()
    {
        await DraftForReviewAsync(UpdatedAt, DraftableOutput);
        await DecideAsync(1, 0, ReviewDecisionKind.Approve);
        var publications = PostgresPublicationStore.FromConnectionString(connectionString);

        await Assert.ThrowsAsync<PublicationConflictException>(() =>
            publications.CommitAsync(PublicationScenario.Owner.Subject, ActionCommit(reviewStateVersion: 0), "stale", ReleaseHash(1), default));
        Assert.Equal(new PublicationState(0, null), await publications.GetStateAsync(default));
        var committed = await publications.CommitAsync(PublicationScenario.Owner.Subject, ActionCommit(reviewStateVersion: 1), "fresh",
            ReleaseHash(2), default);
        Assert.Equal(1, committed.ReleaseNumber);
    }

    [Fact]
    public async Task ACommittedActionLeavesTheReadyListBecauseTheReleaseRowCarriesTheActionKind()
    {
        await DraftAsync(UpdatedAt, DraftableOutput);
        Assert.Single(await ActionReviews().ListUnpublishedApprovedAsync(10, [], default));

        await PostgresPublicationStore.FromConnectionString(connectionString).CommitAsync(PublicationScenario.Owner.Subject,
            ActionCommit(reviewStateVersion: 1), "key", ReleaseHash(1), default);

        Assert.Equal("action", await ScalarAsync("SELECT kind FROM publication_record_states"));
        Assert.Empty(await ActionReviews().ListUnpublishedApprovedAsync(10, [], default));

        await AddSponsorAsync(UpdatedAt.AddDays(1));

        Assert.Single(await ActionReviews().ListUnpublishedApprovedAsync(10, [], default));
    }

    // The statistics describe one draft, as they do after a bulk load. Postgres then counts one published record
    // and joins records row by row, which takes more than 15 s for 15000 drafts.
    [Fact]
    public async Task TheReadyListStaysFastWhenTheActiveReleaseHoldsThousandsOfActionsBecauseItLooksUpOneMapOfPublishedRevisions()
    {
        await DraftAsync(UpdatedAt, DraftableOutput);
        await ExecuteAsync(connectionString, """
            ALTER TABLE action_drafts SET (autovacuum_enabled = false);
            ALTER TABLE action_review_decisions SET (autovacuum_enabled = false);
            ALTER TABLE publication_releases SET (autovacuum_enabled = false);
            ALTER TABLE publication_record_states SET (autovacuum_enabled = false);
            INSERT INTO publication_releases (release_number, directory_name, actor_subject, published_at_utc_ticks, record_count, is_active)
            VALUES (1, '000001-' || md5('bulk'), 'bulk', 0, 15000, true);
            ANALYZE action_drafts, action_review_decisions, publication_releases, publication_record_states;
            CREATE TEMP TABLE seeded AS
            SELECT md5(n::text) || md5('x' || n::text) AS record_id, n FROM generate_series(1, 15000) n;
            INSERT INTO action_drafts (record_id, source_id, congress, bill_type, bill_number, current_revision_number, created_at_utc_ticks,
                last_revised_at_utc_ticks, review_state_version, review_status)
            SELECT record_id, 'bulk', 119, 'hr', n, 1, 0, 0, 1, 'Approved' FROM seeded;
            INSERT INTO action_revisions (record_id, revision_number, author_subject, author_kind, revision_json, created_at_utc_ticks)
            SELECT record_id, 1, 'bulk', 'Person', '{}', 0 FROM seeded;
            INSERT INTO action_review_decisions (decision_id, record_id, revision_number, review_state_version, kind,
                actor_subject, created_at_utc_ticks)
            SELECT md5(record_id), record_id, 1, 1, 'Approve', 'civic-lens:code', 0 FROM seeded;
            INSERT INTO publication_record_states (kind, record_id, revision_number, first_release)
            SELECT 'action', record_id, 1, 1 FROM seeded;
            """);

        var clock = Stopwatch.StartNew();
        var ready = Assert.Single(await ActionReviews().ListUnpublishedApprovedAsync(10, [], default));
        clock.Stop();

        Assert.Equal(RecordId, ready.Action.RecordId);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5), $"The ready list took {clock.Elapsed}.");
    }

    [Fact]
    public async Task TheReadyListLeavesOutTheExcludedIdsInSqlSoSkippedRecordsNeverFillItsLimit()
    {
        await DraftAsync(UpdatedAt, DraftableOutput);

        Assert.Empty(await ActionReviews().ListUnpublishedApprovedAsync(1, [RecordId], default));
        Assert.Single(await ActionReviews().ListUnpublishedApprovedAsync(1, [new string('0', 64)], default));
    }

    [Fact]
    public async Task PublicationReadReturnsTheApprovedActionWithEachCitedExtractionAndOmitsUnknownIds()
    {
        await DraftForReviewAsync(UpdatedAt, DraftableOutput);
        await DecideAsync(1, 0, ReviewDecisionKind.Approve);

        var found = await ActionReviews().GetForPublicationAsync([RecordId, new string('f', 64)], default);

        var read = Assert.Single(found);
        Assert.Equal(RecordId, read.Key);
        Assert.Equal(ReviewStatus.Approved, read.Value.Review.CurrentStatus);
        Assert.Equal(read.Value.Review.CurrentRevision.Citations.Select(span => span.ExtractionId).Distinct().Order(),
            read.Value.Extractions.Keys.Order());
    }

    [Fact]
    public async Task TheStatusOfAnActionIsTheLatestActionOfTheNewestVersionOfItsBillSoAnOlderStatusNeverShowsOnceTheBillMoved()
    {
        await DraftForReviewAsync(UpdatedAt, DraftableOutput);
        await ExecuteAsync(connectionString, "UPDATE bill_gates SET latest_action_date = '2025-03-02', latest_action_text = 'Passed House.'");

        var statuses = await ActionReviews().ReadBillStatusesAsync([RecordId, new string('f', 64)], default);

        var status = Assert.Single(statuses);
        Assert.Equal((RecordId, "Passed House.", new DateOnly(2025, 3, 2)), (status.RecordId, status.Text, status.Date));
        Assert.Equal((long)(await ScalarAsync("SELECT created_at_utc_ticks FROM bill_gates"))!, status.ObservedAtUtc.UtcTicks);
        var newer = await EnqueueDetailAsync("newer");
        await AdmitDetailAsync(newer, DetailBill(UpdatedAt.AddDays(1), cosponsors: 0), BillGateOutcome.Passed(BillGateReason.CoveredSponsor));
        Assert.Empty(await ActionReviews().ReadBillStatusesAsync([RecordId], default));
        Assert.Empty(await ActionReviews().ReadBillStatusesAsync([], default));
    }

    // The saved Congress.gov pages pass the contract check, so a drafted citation never blocks a publish.
    [Fact]
    public async Task AnApprovedDraftedActionPublishesWithEveryCitationQuotingItsSavedApiPage()
    {
        await DraftForReviewAsync(UpdatedAt, DraftableOutput);
        await DecideAsync(1, 0, ReviewDecisionKind.Approve);
        var revision = (await ActionReviews().GetRevisionAsync(RecordId, 1, default))!;
        var names = revision.Sponsorships.Select(item => item.PersonId).Distinct().ToDictionary(id => id, id => $"Official {id}");
        var releases = new FileReleaseDirectory(Path.Combine(captureRoot, "releases"));
        var store = PostgresPublicationStore.FromConnectionString(connectionString);
        var publisher = new PublishRelease(store, releases, new ReleaseRenderer(null, null),
            new PublishedRecordLoaders(PostgresDocumentChangeReviewStore.FromConnectionString(connectionString),
                PostgresStatementReviewStore.FromConnectionString(connectionString), ActionReviews(),
                PostgresVoteReviewStore.FromConnectionString(connectionString),
                new PostgresRelationshipReviewStore(AnalysisDbContextFactory.Create(connectionString)), new PostgresBriefReviewStore(AnalysisDbContextFactory.Create(connectionString)),
                PostgresExecutiveActionReviewStore.FromConnectionString(connectionString)),
            new PublicationCatalog(names, IssueConfiguration.None), FixedRosters.NamedOnly(names.Keys), PhotoCaptures.None(), TimeProvider.System,
            new PruneReleases(store, releases, NullLogger<PruneReleases>.Instance));

        var summary = await publisher.ExecuteAsync(PublicationScenario.Owner,
            PublicationScenario.Request("publish", new PublishedRecordReference(PublishedRecordKind.Action, RecordId)));

        var record = JsonSerializer.Deserialize<PublishedAction>(await releases.ReadFileAsync(summary.DirectoryName,
            PublicationProtocol.RecordDataPath(RecordId), PublicationProtocol.MaximumManifestBytes, default), PublicationProtocol.JsonOptions)!;
        record.Validate();
        Assert.Equal(revision.Citations.Select(span => span.ExtractionId).Distinct(), record.Sources.Select(source => source.ExtractionId));
        Assert.Equal(revision.Sponsorships.Length, record.Sponsorships.Length);
    }

    [Fact]
    public async Task ACodeWrittenActionSettlesWithAFreeBypassedRunIsCodeApprovedAndPublishesWithItsLabelOriginAndCitedBillPage()
    {
        _ = await ReadableCandidateAsync(sponsor: "A000001", policyArea: BillGateFixtures.UntrackedPolicyArea, withSummary: false);

        var model = await RunActionWorkerAsync(new FakeHeadlineSummaryModel());

        Assert.Empty(model.Requests);
        Assert.Equal(("Bypassed", "0 codeWritten"), await TupleAsync(
            "SELECT outcome, charged_tokens || ' ' || reason FROM action_analysis_runs"));
        Assert.Equal(("Code", "civic-lens:code"), await TupleAsync($"SELECT author_kind, author_subject FROM action_revisions WHERE record_id = '{RecordId}'"));
        var decision = Assert.Single(await ActionDecisionsAsync());
        Assert.Equal((ReviewDecisionKind.Approve, ReviewAuthor.CodeSubject), (decision.Kind, decision.ActorSubject));
        var revision = (await ActionReviews().GetRevisionAsync(RecordId, 1, default))!;

        var record = await PublishActionAsync(revision);

        Assert.Equal(PublishedTextOrigin.Code, record.TextOrigin);
        Assert.Contains(record.Sources, source => source.ExtractionId == revision.BillCitation!.ExtractionId);
        Assert.Contains("Congress.gov has no summary of this bill yet.", record.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ACodeWrittenActionWithOnlyACosponsorAndNoPolicyAreaPublishesBecauseItsBillPageIsCited()
    {
        _ = await ReadableCandidateAsync(sponsor: "Z000009", cosponsor: "B000002", policyArea: null, withSummary: false);
        _ = await RunActionWorkerAsync(new FakeHeadlineSummaryModel());
        var revision = (await ActionReviews().GetRevisionAsync(RecordId, 1, default))!;

        var record = await PublishActionAsync(revision);

        Assert.All(revision.Sponsorships, sponsorship => Assert.Equal(ActionSponsorshipRole.Cosponsor, sponsorship.Role));
        Assert.Null(record.PolicyAreaCitation);
        Assert.Equal(revision.BillCitation!.ExtractionId, record.BillCitation!.ExtractionId);
        Assert.Contains(record.Sources, source => source.ExtractionId == record.BillCitation.ExtractionId);
    }

    [Fact]
    public async Task ACodeWrittenRecordWithAnIssueCallHoldsTheDraftedRunAndPublishesItsIssues()
    {
        _ = await ReadableCandidateAsync(sponsor: "A000001", withSummary: false);

        var model = await RunActionWorkerAsync(new FakeHeadlineSummaryModel().Draft(HeadlineSummaryOutputsIssue("rent")));

        Assert.Single(model.Requests);
        var run = (string)(await ScalarAsync("SELECT run_id FROM action_analysis_runs WHERE outcome = 'Drafted'"))!;
        Assert.Equal(("Code", run), await TupleAsync($"SELECT author_kind, run_id FROM action_revisions WHERE record_id = '{RecordId}'"));
        Assert.Equal(("action-issue-prompt-v3", "Drafted"), await TupleAsync("SELECT prompt_version, outcome FROM action_analysis_runs"));
        var revision = (await ActionReviews().GetRevisionAsync(RecordId, 1, default))!;
        Assert.Equal(["rent"], revision.TrackedIssueIds.ToArray());
        Assert.Single(await ActionDecisionsAsync());

        var record = await PublishActionAsync(revision);

        Assert.Equal(PublishedTextOrigin.Code, record.TextOrigin);
        Assert.Equal(["rent"], record.TrackedIssueIds);
    }

    private static string HeadlineSummaryOutputsIssue(params string[] ids) => JsonSerializer.Serialize(new { issueIds = ids, confidence = 0.8 });

    private async Task<PublishedAction> PublishActionAsync(ActionDraftRevision revision)
    {
        var names = revision.Sponsorships.Select(item => item.PersonId).Distinct().ToDictionary(id => id, id => $"Official {id}");
        var releases = new FileReleaseDirectory(Path.Combine(captureRoot, "releases"));
        var publications = PostgresPublicationStore.FromConnectionString(connectionString);
        var publisher = new PublishRelease(publications, releases, new ReleaseRenderer(null, null),
            new PublishedRecordLoaders(PostgresDocumentChangeReviewStore.FromConnectionString(connectionString),
                PostgresStatementReviewStore.FromConnectionString(connectionString), ActionReviews(),
                PostgresVoteReviewStore.FromConnectionString(connectionString),
                new PostgresRelationshipReviewStore(AnalysisDbContextFactory.Create(connectionString)), new PostgresBriefReviewStore(AnalysisDbContextFactory.Create(connectionString)),
                PostgresExecutiveActionReviewStore.FromConnectionString(connectionString)),
            new PublicationCatalog(names, IssueConfiguration.None), FixedRosters.NamedOnly(names.Keys), PhotoCaptures.None(), TimeProvider.System,
            new PruneReleases(publications, releases, NullLogger<PruneReleases>.Instance));
        var summary = await publisher.ExecuteAsync(PublicationScenario.Owner,
            PublicationScenario.Request("publish", new PublishedRecordReference(PublishedRecordKind.Action, RecordId)));
        var record = JsonSerializer.Deserialize<PublishedAction>(await releases.ReadFileAsync(summary.DirectoryName,
            PublicationProtocol.RecordDataPath(RecordId), PublicationProtocol.MaximumManifestBytes, default), PublicationProtocol.JsonOptions)!;
        record.Validate();
        return record;
    }

    private static PublicationCommit ActionCommit(int reviewStateVersion) =>
        new(1, null, $"000001-{Guid.NewGuid():N}", [new(PublishedRecordKind.Action, RecordId, 1)],
            [new(PublishedRecordKind.Action, RecordId, 1, reviewStateVersion)]);

    private static string ReleaseHash(int seed) => Convert.ToHexStringLower(SHA256.HashData(BitConverter.GetBytes(seed)));
}
