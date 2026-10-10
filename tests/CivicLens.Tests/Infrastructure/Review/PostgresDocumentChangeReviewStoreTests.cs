using System.Data.Common;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CivicLens.Application.Paging;
using CivicLens.Application.Publication;
using CivicLens.Application.Review;
using CivicLens.Application.Collection;
using CivicLens.Collection.Contracts;
using CivicLens.Core.Review;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Documents;
using CivicLens.Infrastructure.Review;
using CivicLens.Tests.Host;
using CivicLens.Tests.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql;
using CivicLens.Tests.Fixtures;
using static CivicLens.Tests.Fixtures.CollectionFixtures;

namespace CivicLens.Tests.Infrastructure.Review;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class PostgresDocumentChangeReviewStoreTests(PostgresCollection postgres) : IAsyncLifetime
{
    private readonly string schema = "review_" + Guid.NewGuid().ToString("N");
    private readonly string captureRoot = Path.Combine(Path.GetTempPath(), "civic-review-" + Guid.NewGuid().ToString("N"));
    private string connectionString = null!;
    private PooledDbContextFactory<CollectionAttemptDbContext> factory = null!;
    private PostgresCollectionAttemptStore attempts = null!;
    private PostgresDocumentExtractionStore extractions = null!;
    private PostgresDocumentComparisonStore comparisons = null!;
    private PostgresDocumentChangeReviewStore reviews = null!;

    public async Task InitializeAsync()
    {
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"CREATE SCHEMA {schema}";
        await command.ExecuteNonQueryAsync();
        connectionString = new NpgsqlConnectionStringBuilder(postgres.ConnectionString) { SearchPath = schema, Pooling = false }.ConnectionString;
        factory = new PooledDbContextFactory<CollectionAttemptDbContext>(
            new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString).Options);
        attempts = new PostgresCollectionAttemptStore(factory);
        extractions = new PostgresDocumentExtractionStore(factory);
        comparisons = new PostgresDocumentComparisonStore(factory);
        reviews = new PostgresDocumentChangeReviewStore(factory);
        await attempts.MigrateAsync();
        Directory.CreateDirectory(captureRoot);
    }

    public async Task DisposeAsync()
    {
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"DROP SCHEMA IF EXISTS {schema} CASCADE";
        await command.ExecuteNonQueryAsync();
        Directory.Delete(captureRoot, recursive: true);
    }

    [Fact]
    public async Task CreateReplayIsStableAndConcurrentSavesAppendOnlyOneCurrentRevision()
    {
        var comparison = await SaveComparisonAsync();
        var actor = new ReviewActor("auth0|owner", ReviewRole.Owner);
        var catalog = new ReviewCatalog([], []);
        var create = new CreateDocumentChangeDraft(reviews);
        var createRequest = new CreateDocumentChangeDraftRequest(comparison.ComparisonId, "create-key");
        var first = await create.ExecuteAsync(actor, createRequest);
        var replay = await create.ExecuteAsync(actor, createRequest);
        Assert.Equal(first.DraftId, replay.DraftId);
        Assert.NotEmpty(replay.Citations);

        var save = new SaveDocumentChangeDraft(reviews, catalog);
        var initialRequest = new SaveDocumentChangeDraftRequest(first.DraftId, 1, "Policy update", "The policy changed.",
            null, "The record does not establish why the source changed.", "Example Office", null, null,
            [], [], first.Citations, "save-a");
        var firstSave = save.ExecuteAsync(actor, initialRequest);
        var competingSave = save.ExecuteAsync(actor, initialRequest with { Headline = "Competing edit", IdempotencyKey = "save-b" });
        var outcomes = await Task.WhenAll(ObserveAsync(firstSave), ObserveAsync(competingSave));
        Assert.Single(outcomes, outcome => outcome is DocumentChangeDraftRevision);
        Assert.Single(outcomes, outcome => outcome is DocumentChangeReviewConflictException);

        var review = await reviews.GetAsync(first.DraftId, CancellationToken.None);
        Assert.NotNull(review);
        Assert.Equal(2, review.Revisions.Length);
        Assert.Equal(2, review.CurrentRevision.RevisionNumber);
    }

    [Fact]
    public async Task DecisionReplayReturnsOriginalDecisionAndRejectsPayloadReuse()
    {
        var comparison = await SaveComparisonAsync();
        var actor = new ReviewActor("auth0|reviewer", ReviewRole.Reviewer);
        var catalog = new ReviewCatalog([], []);
        var revision = await new CreateDocumentChangeDraft(reviews).ExecuteAsync(actor,
            new(comparison.ComparisonId, "create-key"));
        revision = await new SaveDocumentChangeDraft(reviews, catalog).ExecuteAsync(actor,
            new(revision.DraftId, 1, "Policy update", "The policy changed.", null, null, "Example Office",
                null, null, [], [], revision.Citations, "save-key"));
        var decide = new DecideDocumentChangeReview(reviews, catalog);
        var request = new DecideDocumentChangeReviewRequest(revision.DraftId, 2, 0,
            ReviewDecisionKind.RequestChanges, "Please explain the before and after context.", [], "decision-key");
        var first = await decide.ExecuteAsync(actor, request);
        var replay = await decide.ExecuteAsync(actor, request);
        Assert.Equal(first.DecisionId, replay.DecisionId);
        await Assert.ThrowsAsync<ArgumentException>(() => decide.ExecuteAsync(actor,
            request with { Note = "Different payload." }));
        var detail = await reviews.GetAsync(revision.DraftId, CancellationToken.None);
        Assert.Equal(first.DecisionId, Assert.Single(detail!.UnresolvedConcerns).DecisionId);
    }

    [Fact]
    public async Task ComparisonPagesTraverseIneligibleRowsWithoutOmissionsOrDuplicates()
    {
        var expected = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < 12; index++)
        {
            var before = new CivicLens.Core.Documents.DocumentExtraction(await ImportAsync("Before."), "parser", "normalizer", "Before.");
            var changed = index % 3 == 0;
            var text = changed ? "After." : "Before.";
            var after = new CivicLens.Core.Documents.DocumentExtraction(await ImportAsync(text), "parser", "normalizer", text);
            await extractions.SaveAsync(before, default);
            await extractions.SaveAsync(after, default);
            var comparison = await comparisons.SaveAsync(CivicLens.Core.Documents.DocumentComparison.Create(before, after), default);
            if (changed) expected.Add(comparison.ComparisonId);
        }
        var actual = new List<string>();
        PageCursor? cursor = null;
        var pages = 0;
        do
        {
            var page = await reviews.ListEligibleComparisonsAsync(cursor, 1, default);
            actual.AddRange(page.Items.Select(item => item.Comparison.ComparisonId));
            cursor = PageCursor.Parse(page.OlderCursor);
            Assert.True(++pages <= 12);
        } while (cursor is not null);
        Assert.Equal(expected.Order(), actual.Order());
        Assert.Equal(actual.Count, actual.Distinct().Count());
    }

    [Fact]
    public async Task EvidenceRangesAndApprovalRequirementsAreEnforced()
    {
        var comparison = await SaveComparisonAsync();
        var actor = new ReviewActor("auth0|owner", ReviewRole.Owner);
        var catalog = new ReviewCatalog([], []);
        var revision = await new CreateDocumentChangeDraft(reviews).ExecuteAsync(actor, new(comparison.ComparisonId, "create"));
        var decide = new DecideDocumentChangeReview(reviews, catalog);
        await Assert.ThrowsAsync<ArgumentException>(() => decide.ExecuteAsync(actor,
            new(revision.DraftId, 1, 0, ReviewDecisionKind.Approve, null, [], "incomplete")));
        var request = new SaveDocumentChangeDraftRequest(revision.DraftId, 1, "Headline", "Summary", null, null,
            "Institution", null, null, [], [], [new(comparison.BeforeExtractionId, int.MaxValue, 1)], "bad-range");
        await Assert.ThrowsAsync<ArgumentException>(() => new SaveDocumentChangeDraft(reviews, catalog).ExecuteAsync(actor, request));
        request = request with { Citations = revision.Citations, IdempotencyKey = "valid" };
        var saved = await new SaveDocumentChangeDraft(reviews, catalog).ExecuteAsync(actor, request);
        await decide.ExecuteAsync(actor, new(saved.DraftId, 2, 0, ReviewDecisionKind.Approve, null, [], "approve"));
        var concern = await decide.ExecuteAsync(actor, new(saved.DraftId, 2, 1, ReviewDecisionKind.WithdrawApproval, "Context missing", [], "withdraw"));
        await Assert.ThrowsAsync<ArgumentException>(() => decide.ExecuteAsync(actor,
            new(saved.DraftId, 2, 2, ReviewDecisionKind.Approve, null, [], "unresolved")));
        await decide.ExecuteAsync(actor, new(saved.DraftId, 2, 2, ReviewDecisionKind.Approve, "Context checked", [concern.DecisionId], "resolved"));
        var detail = await reviews.GetAsync(saved.DraftId, default);
        Assert.Empty(detail!.UnresolvedConcerns);
        Assert.Equal(actor.Subject, detail.CurrentRevision.AuthorSubject);
    }

    [Fact]
    public async Task QueueRejectsApprovalThatDoesNotResolveRetainedConcern()
    {
        var comparison = await SaveComparisonAsync();
        var actor = new ReviewActor("auth0|owner", ReviewRole.Owner);
        var catalog = new ReviewCatalog([], []);
        var revision = await new CreateDocumentChangeDraft(reviews).ExecuteAsync(actor,
            new(comparison.ComparisonId, "create"));
        revision = await new SaveDocumentChangeDraft(reviews, catalog).ExecuteAsync(actor,
            new(revision.DraftId, 1, "Headline", "Summary", null, null, "Institution",
                null, null, [], [], revision.Citations, "save"));
        var decide = new DecideDocumentChangeReview(reviews, catalog);
        var concern = await decide.ExecuteAsync(actor,
            new(revision.DraftId, 2, 0, ReviewDecisionKind.RequestChanges, "Check context.", [], "concern"));
        var approval = await decide.ExecuteAsync(actor,
            new(revision.DraftId, 2, 1, ReviewDecisionKind.Approve, "Context checked.", [concern.DecisionId], "approve"));

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            UPDATE document_change_review_decisions
            SET decision_json = jsonb_set(decision_json::jsonb, '{resolvedDecisionIds}', '[]'::jsonb)::text
            WHERE decision_id = @id
            """, connection);
        command.Parameters.AddWithValue("id", approval.DecisionId);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());

        await Assert.ThrowsAsync<ArgumentException>(() => reviews.GetAsync(revision.DraftId, default));
        await Assert.ThrowsAsync<ArgumentException>(() => reviews.ListAsync(null, 10, DraftStatusFilter.All, default));
    }

    [Fact]
    public async Task PublicationReadReturnsTheSameVerifiedReviewAsTheSingleDraftRead()
    {
        var drafts = await CreateDraftsAsync(3);
        var missing = new string('f', 32);

        var batch = await reviews.GetForPublicationAsync([.. drafts.Select(draft => draft.DraftId), missing], default);

        Assert.Equal(3, batch.Count);
        Assert.False(batch.ContainsKey(missing));
        foreach (var draft in drafts)
        {
            var change = batch[draft.DraftId];
            var single = await reviews.GetAsync(draft.DraftId, default);
            Assert.Equal(JsonSerializer.Serialize(single), JsonSerializer.Serialize(change.Review));
            Assert.Equal(draft.ComparisonId, change.Comparison.Comparison.ComparisonId);
            Assert.Equal(change.Comparison.Comparison.BeforeExtractionId, change.Before.ExtractionId);
            Assert.Equal(change.Comparison.Comparison.AfterExtractionId, change.After.ExtractionId);
            Assert.Equal("Policy before.", change.Before.Text);
            Assert.Equal("Policy after.", change.After.Text);
        }
    }

    [Fact]
    public async Task PublicationReadAcceptsTheLargestPublishWithTheSameQueryCountAsOneDraft()
    {
        var drafts = await CreateDraftsAsync(PublishDocumentChanges.MaximumDrafts);
        var counter = new ReadCommandCounter();
        var counted = CountedStore(counter);

        await counted.GetForPublicationAsync([drafts[0].DraftId], default);
        var singleDraftReads = counter.ReadCount;
        counter.ReadCount = 0;
        var batch = await counted.GetForPublicationAsync([.. drafts.Select(draft => draft.DraftId)], default);

        Assert.Equal(PublishDocumentChanges.MaximumDrafts, batch.Count);
        Assert.Equal(singleDraftReads, counter.ReadCount);
        Assert.InRange(counter.ReadCount, 1, 7);
    }

    [Fact]
    public async Task ReviewListUsesTheSameQueryCountForOneDraftAndAFullPageSoTheQueueStaysCheap()
    {
        await CreateDraftsAsync(20);
        var counter = new ReadCommandCounter();
        var counted = CountedStore(counter);

        await counted.ListAsync(null, 1, DraftStatusFilter.All, default);
        var singleDraftReads = counter.ReadCount;
        counter.ReadCount = 0;
        var page = await counted.ListAsync(null, 20, DraftStatusFilter.All, default);

        Assert.Equal(20, page.Items.Length);
        Assert.Equal(singleDraftReads, counter.ReadCount);
    }

    [Fact]
    public async Task PublicationReadRejectsTamperedComparisonLikeTheSingleDraftReadSoAlteredEvidenceIsNeverPublished()
    {
        var drafts = await CreateDraftsAsync(2);
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "UPDATE document_comparisons SET settings_version = 'tampered' WHERE comparison_id = @id", connection);
        command.Parameters.AddWithValue("id", drafts[1].ComparisonId);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());

        await Assert.ThrowsAsync<InvalidOperationException>(() => reviews.GetAsync(drafts[1].DraftId, default));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            reviews.GetForPublicationAsync([.. drafts.Select(draft => draft.DraftId)], default));
    }

    [Fact]
    public async Task DraftPagesWalkOlderThenNewerOverTheSameRowsEvenWhenTimestampsTie()
    {
        var drafts = await CreateDraftsAsync(7);
        // Three drafts share each timestamp, so only the id tie-breaker separates them.
        var keyed = drafts.Select((draft, index) => (draft.DraftId, Ticks: 5_000L + index / 3)).ToArray();
        await SetDraftTicksAsync(keyed);
        var expected = keyed.OrderByDescending(key => key.Ticks).ThenByDescending(key => key.DraftId, StringComparer.Ordinal)
            .Select(key => key.DraftId).ToArray();

        var walk = await PageWalk.RunAsync(async cursor =>
        {
            var page = await reviews.ListAsync(cursor, 2, DraftStatusFilter.All, default);
            return (page.Items.Select(item => item.DraftId).ToArray(), page.NewerCursor, page.OlderCursor);
        });

        walk.AssertExact(expected);
    }

    [Fact]
    public async Task ComparisonPagesWalkOlderThenNewerOverTheSameRowsEvenWhenTimestampsTie()
    {
        var ids = new List<string>();
        for (var index = 0; index < 7; index++) ids.Add((await SaveComparisonAsync()).ComparisonId);
        var keyed = ids.Select((id, index) => (Id: id, Ticks: 7_000L + index / 3)).ToArray();
        foreach (var key in keyed) await SetAfterObservedTicksAsync(key.Id, key.Ticks);
        var expected = keyed.OrderByDescending(key => key.Ticks).ThenByDescending(key => key.Id, StringComparer.Ordinal)
            .Select(key => key.Id).ToArray();

        var walk = await PageWalk.RunAsync(async cursor =>
        {
            var page = await reviews.ListEligibleComparisonsAsync(cursor, 2, default);
            return (page.Items.Select(item => item.Comparison.ComparisonId).ToArray(), page.NewerCursor, page.OlderCursor);
        });

        walk.AssertExact(expected);
    }

    [Fact]
    public async Task ComparisonsThatAlreadyHaveADraftAreNotNewChanges()
    {
        var drafted = await SaveComparisonAsync();
        var open = await SaveComparisonAsync();
        await new CreateDocumentChangeDraft(reviews).ExecuteAsync(new("auth0|owner", ReviewRole.Owner), new(drafted.ComparisonId, "key"));

        var page = await reviews.ListEligibleComparisonsAsync(null, 10, default);

        Assert.Equal(open.ComparisonId, Assert.Single(page.Items).Comparison.ComparisonId);
        Assert.Equal(1, (await reviews.GetOverviewAsync(default)).NewChanges);
    }

    // The newest drafts are approved. If the filter ran after the limit, the first needs-action page would come back short.
    [Fact]
    public async Task StatusFilterAppliesBeforeTheLimitAndNeedsActionIsEveryStatusButApproved()
    {
        var seeded = await SeedEveryStatusAsync();

        var needsAction = await reviews.ListAsync(null, 2, DraftStatusFilter.NeedsAction, default);
        var approved = await reviews.ListAsync(null, 1, DraftStatusFilter.Approved, default);

        Assert.Equal([seeded.ChangesRequested, seeded.Withdrawn], needsAction.Items.Select(item => item.DraftId));
        Assert.Equal(DocumentChangeReviewStatus.ChangesRequested, needsAction.Items[0].CurrentStatus);
        Assert.Equal(DocumentChangeReviewStatus.ApprovalWithdrawn, needsAction.Items[1].CurrentStatus);
        Assert.NotNull(needsAction.OlderCursor);
        var rest = await reviews.ListAsync(PageCursor.Parse(needsAction.OlderCursor), 10, DraftStatusFilter.NeedsAction, default);
        Assert.Equal([DocumentChangeReviewStatus.AwaitingReviewWithConcerns, DocumentChangeReviewStatus.AwaitingReview],
            rest.Items.Select(item => item.CurrentStatus));
        Assert.Null(rest.OlderCursor);
        Assert.Equal(seeded.ApprovedNewest, Assert.Single(approved.Items).DraftId);
        Assert.NotNull(approved.OlderCursor);
        var all = await reviews.ListAsync(null, 10, DraftStatusFilter.All, default);
        Assert.Equal(6, all.Items.Length);
    }

    [Fact]
    public async Task OverviewCountsEqualTheTotalsOfTheirLists()
    {
        await SeedEveryStatusAsync();
        await SaveComparisonAsync();
        await SaveComparisonAsync();

        var overview = await reviews.GetOverviewAsync(default);

        var newChanges = await CountAsync(async cursor =>
        {
            var page = await reviews.ListEligibleComparisonsAsync(cursor, 1, default);
            return (page.Items.Length, page.OlderCursor);
        });
        var needsAction = await CountAsync(async cursor =>
        {
            var page = await reviews.ListAsync(cursor, 3, DraftStatusFilter.NeedsAction, default);
            return (page.Items.Length, page.OlderCursor);
        });
        var approved = await CountAsync(async cursor =>
        {
            var page = await reviews.ListAsync(cursor, 1, DraftStatusFilter.Approved, default);
            return (page.Items.Length, page.OlderCursor);
        });
        Assert.Equal(new ReviewOverview(2, 4, 2), overview);
        Assert.Equal((overview.NewChanges, overview.DraftsNeedingAction, overview.ApprovedDrafts), (newChanges, needsAction, approved));
    }

    [Fact]
    public async Task RecentActivityTakesTheNewestOfEachKindWithActorAndHeadline()
    {
        var seeded = await SeedEveryStatusAsync();

        var events = await reviews.ListRecentActivityAsync(2, default);

        Assert.Equal(2, events.Count(item => item.Kind == ReviewActivityKind.DraftCreated));
        Assert.Equal(2, events.Count(item => item.Kind == ReviewActivityKind.RevisionSaved));
        Assert.Equal(2, events.Count(item => item.Kind == ReviewActivityKind.DecisionRecorded));
        var created = events.Where(item => item.Kind == ReviewActivityKind.DraftCreated).OrderByDescending(item => item.OccurredAt).ToArray();
        Assert.Equal([seeded.ApprovedNewest, seeded.ApprovedOlder], created.Select(item => item.DraftId));
        Assert.All(events, item => Assert.Equal("auth0|owner", item.ActorSubject));
        Assert.All(events.Where(item => item.Kind != ReviewActivityKind.DraftCreated), item => Assert.Equal("Headline", item.Headline));
        Assert.All(events.Where(item => item.Kind == ReviewActivityKind.DecisionRecorded), item => Assert.NotNull(item.DecisionKind));
        Assert.All(events.Where(item => item.Kind != ReviewActivityKind.DecisionRecorded), item => Assert.Null(item.DecisionKind));
    }

    [Fact]
    public async Task ListReadCountDoesNotGrowWithPageSizeOrFilter()
    {
        await SeedEveryStatusAsync();
        var counter = new ReadCommandCounter();
        var counted = CountedStore(counter);

        await counted.ListAsync(null, 1, DraftStatusFilter.NeedsAction, default);
        var single = counter.ReadCount;
        counter.ReadCount = 0;
        await counted.ListAsync(null, 20, DraftStatusFilter.All, default);

        Assert.Equal(single, counter.ReadCount);
    }

    private sealed record SeededStatuses(string ApprovedNewest, string ApprovedOlder, string ChangesRequested,
        string Withdrawn, string WithConcerns, string Awaiting);

    // Oldest to newest: awaiting, awaiting with concerns, approval withdrawn, changes requested, approved, approved.
    private async Task<SeededStatuses> SeedEveryStatusAsync()
    {
        var actor = new ReviewActor("auth0|owner", ReviewRole.Owner);
        var catalog = new ReviewCatalog([], []);
        var save = new SaveDocumentChangeDraft(reviews, catalog);
        var decide = new DecideDocumentChangeReview(reviews, catalog);
        var drafts = await CreateDraftsAsync(6);
        await SetDraftTicksAsync(drafts.Select((draft, index) => (draft.DraftId, Ticks: 9_000L + index)).ToArray());

        async Task<DocumentChangeDraftRevision> Edit(DocumentChangeDraftRevision draft, int from, string key) =>
            await save.ExecuteAsync(actor, new(draft.DraftId, from, "Headline", "Summary", null, null, "Institution",
                null, null, [], [], draft.Citations, key));

        var concerned = await Edit(drafts[1], 1, "edit-1");
        await decide.ExecuteAsync(actor, new(concerned.DraftId, 2, 0, ReviewDecisionKind.RequestChanges, "Context missing.", [], "concern-1"));
        await Edit(concerned, 2, "edit-1b");
        var withdrawn = await Edit(drafts[2], 1, "edit-2");
        await decide.ExecuteAsync(actor, new(withdrawn.DraftId, 2, 0, ReviewDecisionKind.Approve, null, [], "approve-2"));
        await decide.ExecuteAsync(actor, new(withdrawn.DraftId, 2, 1, ReviewDecisionKind.WithdrawApproval, "Wrong date.", [], "withdraw-2"));
        var requested = await Edit(drafts[3], 1, "edit-3");
        await decide.ExecuteAsync(actor, new(requested.DraftId, 2, 0, ReviewDecisionKind.RequestChanges, "Needs sources.", [], "request-3"));
        foreach (var index in new[] { 4, 5 })
        {
            var approved = await Edit(drafts[index], 1, "edit-" + index);
            await decide.ExecuteAsync(actor, new(approved.DraftId, 2, 0, ReviewDecisionKind.Approve, null, [], "approve-" + index));
        }
        return new(drafts[5].DraftId, drafts[4].DraftId, drafts[3].DraftId, drafts[2].DraftId, drafts[1].DraftId, drafts[0].DraftId);
    }

    private static async Task<int> CountAsync(Func<PageCursor?, Task<(int Count, string? Older)>> read)
    {
        var total = 0;
        PageCursor? cursor = null;
        for (var guard = 0; guard < 50; guard++)
        {
            var (count, older) = await read(cursor);
            total += count;
            if (older is null) return total;
            cursor = PageCursor.Parse(older);
        }
        throw new InvalidOperationException("Paging did not end.");
    }

    private async Task SetDraftTicksAsync((string DraftId, long Ticks)[] keys)
    {
        foreach (var key in keys)
            await SqlAsync("UPDATE document_change_drafts SET created_at_utc_ticks = @ticks WHERE draft_id = @id",
                ("ticks", key.Ticks), ("id", key.DraftId));
    }

    private Task SetAfterObservedTicksAsync(string comparisonId, long ticks) => SqlAsync("""
        UPDATE collection_attempts SET observed_at_utc_ticks = @ticks
         WHERE attempt_id IN (SELECT e.attempt_id FROM document_extractions e
                              JOIN document_comparisons c ON c.after_extraction_id = e.extraction_id
                              WHERE c.comparison_id = @id)
        """, ("ticks", ticks), ("id", comparisonId));

    private async Task SqlAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
        Assert.True(await command.ExecuteNonQueryAsync() > 0);
    }

    private async Task<List<DocumentChangeDraftRevision>> CreateDraftsAsync(int count)
    {
        var actor = new ReviewActor("auth0|owner", ReviewRole.Owner);
        var create = new CreateDocumentChangeDraft(reviews);
        var drafts = new List<DocumentChangeDraftRevision>(count);
        for (var index = 0; index < count; index++)
        {
            var comparison = await SaveComparisonAsync();
            drafts.Add(await create.ExecuteAsync(actor, new(comparison.ComparisonId, "create-" + index)));
        }
        return drafts;
    }

    private async Task<CivicLens.Core.Documents.DocumentComparison> SaveComparisonAsync()
    {
        var before = new CivicLens.Core.Documents.DocumentExtraction(await ImportAsync("Policy before."), "parser", "normalizer", "Policy before.");
        var after = new CivicLens.Core.Documents.DocumentExtraction(await ImportAsync("Policy after."), "parser", "normalizer", "Policy after.");
        await extractions.SaveAsync(before, CancellationToken.None);
        await extractions.SaveAsync(after, CancellationToken.None);
        return await comparisons.SaveAsync(CivicLens.Core.Documents.DocumentComparison.Create(before, after), CancellationToken.None);
    }

    private async Task<CivicLens.Core.Collection.CapturedAttemptResult> ImportAsync(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        await using (var file = File.Create(Path.Combine(captureRoot, hash + ".gz")))
        await using (var gzip = new GZipStream(file, CompressionLevel.SmallestSize))
            await gzip.WriteAsync(bytes);
        var request = Request() with { JobId = Guid.NewGuid().ToString("N"), ArtifactDirectory = captureRoot };
        var receipt = Receipt(request) with
        {
            BytesReceived = bytes.Length,
            Capture = new CaptureArtifact { Sha256 = hash, RelativePath = hash + ".gz", ByteLength = bytes.Length },
            Response = new HttpResponseMetadata { StatusCode = 200, ContentType = "text/plain; charset=utf-8", ContentEncodings = [] }
        };
        var result = await attempts.ImportAtomicallyAsync(CollectionAttemptImporter.CreateImport(
            request.JobId, request, receipt), CancellationToken.None);
        return (CivicLens.Core.Collection.CapturedAttemptResult)result.AttemptResult;
    }

    private static async Task<object> ObserveAsync(Task<DocumentChangeDraftRevision> task)
    {
        try { return await task; }
        catch (DocumentChangeReviewConflictException exception) { return exception; }
    }

    private PostgresDocumentChangeReviewStore CountedStore(ReadCommandCounter counter) =>
        new(new PooledDbContextFactory<CollectionAttemptDbContext>(
            new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString)
                .AddInterceptors(counter).Options));

    private sealed class ReadCommandCounter : DbCommandInterceptor
    {
        public int ReadCount { get; set; }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            ReadCount++;
            return ValueTask.FromResult(result);
        }
    }

}
