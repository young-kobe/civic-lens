using CivicLens.Infrastructure.ExecutiveActions;
using Microsoft.Extensions.Logging.Abstractions;
using CivicLens.Tests.Fixtures;
using CivicLens.Application.Issues;
using CivicLens.Infrastructure.Relationships;
using CivicLens.Infrastructure.Votes;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using CivicLens.Application.Publication;
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

        Assert.Equal("action", await ScalarAsync("SELECT records_json::jsonb -> 0 ->> 'kind' FROM publication_releases"));
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
            INSERT INTO publication_releases (release_number, directory_name, actor_subject, published_at_utc_ticks, record_count, records_json, is_active)
            VALUES (1, '000001-' || md5('bulk'), 'bulk', 0, 15000, '[]', true);
            ANALYZE action_drafts, action_review_decisions, publication_releases, publication_record_states;
            CREATE TEMP TABLE seeded AS
            SELECT md5(n::text) || md5('x' || n::text) AS record_id, n FROM generate_series(1, 15000) n;
            INSERT INTO action_drafts (record_id, source_id, congress, bill_type, bill_number, current_revision_number, created_at_utc_ticks,
                last_revised_at_utc_ticks, review_state_version, review_status)
            SELECT record_id, 'bulk', 119, 'hr', n, 1, 0, 0, 1, 'Approved' FROM seeded;
            INSERT INTO action_revisions (record_id, revision_number, author_subject, author_kind, revision_json, created_at_utc_ticks)
            SELECT record_id, 1, 'bulk', 'Person', '{}', 0 FROM seeded;
            INSERT INTO action_review_decisions (decision_id, record_id, revision_number, review_state_version, decision_json, kind,
                created_at_utc_ticks)
            SELECT md5(record_id), record_id, 1, 1, '{"actorSubject":"civic-lens:code"}', 'Approve', 0 FROM seeded;
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

    private static PublicationCommit ActionCommit(int reviewStateVersion) =>
        new(1, null, $"000001-{Guid.NewGuid():N}", [new(PublishedRecordKind.Action, RecordId, 1)],
            [new(PublishedRecordKind.Action, RecordId, 1, reviewStateVersion)]);

    private static string ReleaseHash(int seed) => Convert.ToHexStringLower(SHA256.HashData(BitConverter.GetBytes(seed)));
}
