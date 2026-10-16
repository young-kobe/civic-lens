using System.Collections.Immutable;
using System.Security.Cryptography;
using CivicLens.Application.Publication;
using CivicLens.Application.Review;
using CivicLens.Application.Statements;
using CivicLens.Core.Documents;
using CivicLens.Core.Review;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Documents;
using CivicLens.Infrastructure.Publication;
using CivicLens.Infrastructure.Review;
using CivicLens.Infrastructure.Statements;
using CivicLens.Publication.Contracts;
using CivicLens.Tests.Fixtures;
using CivicLens.Tests.Host;
using CivicLens.Tests.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql;

namespace CivicLens.Tests.Infrastructure.Publication;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class PostgresPublicationStoreTests(PostgresCollection postgres) : IAsyncLifetime
{
    private static readonly DateTimeOffset PublishedAt = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);
    private static readonly ReviewActor Owner = new("auth0|owner", ReviewRole.Owner);
    private readonly string captureRoot = Path.Combine(Path.GetTempPath(), "civic-publication-" + Guid.NewGuid().ToString("N"));
    private PooledDbContextFactory<CollectionAttemptDbContext> factory = null!;
    private PostgresCollectionAttemptStore attempts = null!;
    private PostgresDocumentExtractionStore extractions = null!;
    private PostgresDocumentComparisonStore comparisons = null!;
    private PostgresDocumentChangeReviewStore reviews = null!;
    private PostgresPublicationStore publications = null!;
    private PostgresStatementReviewStore statementReviews = null!;
    private PostgresStatementDraftStore statementDrafts = null!;
    private StatementCandidateSeeder statementSeeder = null!;
    private string connectionString = null!;
    private int statementCount;
    private ComparisonSeeder seeder = null!;

    public async Task InitializeAsync()
    {
        connectionString = await postgres.CreateMigratedDatabaseAsync();
        factory = new PooledDbContextFactory<CollectionAttemptDbContext>(
            new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString).Options);
        attempts = new PostgresCollectionAttemptStore(factory);
        extractions = new PostgresDocumentExtractionStore(factory);
        comparisons = new PostgresDocumentComparisonStore(factory);
        reviews = new PostgresDocumentChangeReviewStore(factory);
        publications = new PostgresPublicationStore(factory);
        statementReviews = new PostgresStatementReviewStore(factory);
        statementDrafts = new PostgresStatementDraftStore(factory);
        seeder = new ComparisonSeeder(attempts, extractions, comparisons, captureRoot);
        statementSeeder = new StatementCandidateSeeder(attempts, extractions, connectionString, captureRoot);
        Directory.CreateDirectory(captureRoot);
    }

    public async Task DisposeAsync()
    {
        await postgres.DropDatabaseAsync(connectionString);
        Directory.Delete(captureRoot, recursive: true);
    }

    [Fact]
    public async Task ConcurrentCommitsForTheSameNumberLetExactlyOneWin()
    {
        var draft = await CreateDraftAsync();
        var commits = Enumerable.Range(0, 2).Select(index =>
            ObserveAsync(() => publications.CommitAsync(Owner.Subject, Commit(1, draft, 1, 0), $"key-{index}", Hash(index), default)))
            .ToArray();

        var outcomes = await Task.WhenAll(commits);

        Assert.Single(outcomes, outcome => outcome is PublicationReleaseSummary);
        Assert.Single(outcomes, outcome => outcome is PublicationConflictException);
        Assert.Single(await publications.ListAsync(10, default));
    }

    [Fact]
    public async Task ASaveAfterTheBuildBlocksTheCommitBecauseTheServedRevisionWouldBeStale()
    {
        var draft = await CreateDraftAsync();
        await SaveRevisionAsync(draft);

        await Assert.ThrowsAsync<PublicationConflictException>(() =>
            publications.CommitAsync(Owner.Subject, Commit(1, draft.DraftId, 1, 0), "key", Hash(1), default));
        Assert.Equal(new PublicationState(0, null), await publications.GetStateAsync(default));
    }

    [Fact]
    public async Task ADecisionAfterTheBuildBlocksTheCommitBecauseApprovalMayHaveChanged()
    {
        var draft = await CreateDraftAsync();
        var saved = await SaveRevisionAsync(draft);
        await new DecideDocumentChangeReview(reviews, new ReviewCatalog([], [])).ExecuteAsync(Owner,
            new(saved.DraftId, 2, 0, ReviewDecisionKind.RequestChanges, "Please explain the context.", [], "decision-key"));

        await Assert.ThrowsAsync<PublicationConflictException>(() =>
            publications.CommitAsync(Owner.Subject, Commit(1, saved.DraftId, 2, 0), "key", Hash(1), default));
        var committed = await publications.CommitAsync(Owner.Subject, Commit(1, saved.DraftId, 2, 1), "key-2", Hash(2), default);
        Assert.Equal(1, committed.ReleaseNumber);
    }

    [Fact]
    public async Task ReleaseNumbersMustFollowTheLatestCommittedOneWithoutGaps()
    {
        var draft = await CreateDraftAsync();

        await Assert.ThrowsAsync<PublicationConflictException>(() =>
            publications.CommitAsync(Owner.Subject, Commit(2, draft, 1, 0), "skip", Hash(1), default));
        await publications.CommitAsync(Owner.Subject, Commit(1, draft, 1, 0), "first", Hash(2), default);
        await Assert.ThrowsAsync<PublicationConflictException>(() =>
            publications.CommitAsync(Owner.Subject, Commit(1, draft, 1, 0), "again", Hash(3), default));
    }

    [Fact]
    public async Task ReplayReturnsTheOriginalOutcomeAndRejectsADifferentPayloadForTheSameKey()
    {
        var draft = await CreateDraftAsync();
        Assert.Null(await publications.FindReplayAsync(Owner.Subject, "key", Hash(1), default));
        var first = await publications.CommitAsync(Owner.Subject, Commit(1, draft, 1, 0), "key", Hash(1), default);

        Assert.Equal(first, await publications.FindReplayAsync(Owner.Subject, "key", Hash(1), default));
        Assert.Equal(first, await publications.CommitAsync(Owner.Subject, Commit(2, draft, 1, 0), "key", Hash(1), default));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            publications.FindReplayAsync(Owner.Subject, "key", Hash(2), default));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            publications.CommitAsync(Owner.Subject, Commit(2, draft, 1, 0), "key", Hash(2), default));
        Assert.Null(await publications.FindReplayAsync("auth0|other", "key", Hash(2), default));
        Assert.Single(await publications.ListAsync(10, default));
    }

    [Fact]
    public async Task ReleasesAreReadBackNewestFirstWithTheirPublishTime()
    {
        var draft = await CreateDraftAsync();
        for (var number = 1; number <= 3; number++)
            await publications.CommitAsync(Owner.Subject, Commit(number, draft, 1, 0), $"key-{number}", Hash(number), default);

        Assert.Equal([3, 2], (await publications.ListAsync(2, default)).Select(release => release.ReleaseNumber));
        var second = (await publications.ListAsync(2, default))[1];
        Assert.Equal(1, second.RecordCount);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => publications.ListAsync(0, default));
    }

    [Fact]
    public async Task TheReleaseTimeIsTheDatabaseTimeOfTheCommitSoABriefWeekNeverMissesARelease()
    {
        var draft = await CreateDraftAsync();
        var before = await DatabaseTicksAsync();

        var committed = await publications.CommitAsync(Owner.Subject, Commit(1, draft, 1, 0), "key", Hash(1), default);

        var after = await DatabaseTicksAsync();
        Assert.InRange(committed.PublishedAtUtc.UtcTicks, before, after);
        Assert.Equal(committed.PublishedAtUtc, (await publications.ListAsync(1, default)).Single().PublishedAtUtc);
        Assert.InRange(await StoredPublishedTicksAsync(), before, after);
    }

    [Fact]
    public async Task EachCommitBecomesTheActiveReleaseSoAFailedLinkSwapCannotHideIt()
    {
        var draft = await CreateDraftAsync();
        await publications.CommitAsync(Owner.Subject, Commit(1, draft, 1, 0), "key-1", Hash(1), default);
        var second = await publications.CommitAsync(Owner.Subject, Commit(2, draft, 1, 0), "key-2", Hash(2), default);

        Assert.Equal(new PublicationState(2, second), await publications.GetStateAsync(default));
    }

    [Fact]
    public async Task RollbackKeepsTheLatestNumberButMovesTheActiveRelease()
    {
        var draft = await CreateDraftAsync();
        var first = await publications.CommitAsync(Owner.Subject, Commit(1, draft, 1, 0), "key-1", Hash(1), default);
        await publications.CommitAsync(Owner.Subject, Commit(2, draft, 1, 0), "key-2", Hash(2), default);

        Assert.Equal(first, await publications.ActivateAsync(1, default));
        Assert.Equal(new PublicationState(2, first), await publications.GetStateAsync(default));
        await Assert.ThrowsAsync<ArgumentException>(() => publications.ActivateAsync(9, default));
        Assert.Equal(first, (await publications.GetStateAsync(default)).Active);
    }

    [Fact]
    public async Task ReleaseBuiltBeforeARollbackIsRejectedSoItCannotRestoreWithdrawnRecords()
    {
        var draft = await CreateDraftAsync();
        await publications.CommitAsync(Owner.Subject, Commit(1, draft, 1, 0), "key-1", Hash(1), default);
        await publications.CommitAsync(Owner.Subject, Commit(2, draft, 1, 0), "key-2", Hash(2), default);
        var rolledBack = await publications.ActivateAsync(1, default);

        await Assert.ThrowsAsync<PublicationConflictException>(() =>
            publications.CommitAsync(Owner.Subject, Commit(3, 2, draft.DraftId, 1, 0), "stale", Hash(3), default));
        Assert.Equal(new PublicationState(2, rolledBack), await publications.GetStateAsync(default));
        var third = await publications.CommitAsync(Owner.Subject, Commit(3, 1, draft.DraftId, 1, 0), "fresh", Hash(4), default);
        Assert.Equal(third, (await publications.GetStateAsync(default)).Active);
    }

    [Fact]
    public async Task ActivationWaitsForAnInFlightLinkSwapSoTheLinkEndsAtTheRecordedRelease()
    {
        var draft = await CreateDraftAsync();
        var first = await publications.CommitAsync(Owner.Subject, Commit(1, draft, 1, 0), "key-1", Hash(1), default);
        var second = await publications.CommitAsync(Owner.Subject, Commit(2, draft, 1, 0), "key-2", Hash(2), default);
        var served = new List<string>();
        Task<PublicationReleaseSummary>? rollback = null;

        await publications.ServeActiveAsync(async (directory, _) =>
        {
            rollback = publications.ActivateAsync(1, default);
            await HeldAdvisoryLock.WaitForWaiterAsync(connectionString, "civic-lens-document-change-review");
            Assert.False(rollback.IsCompleted);
            served.Add(directory);
        }, default);
        await rollback!;
        await publications.ServeActiveAsync((directory, _) => { served.Add(directory); return Task.CompletedTask; }, default);

        Assert.Equal([second.DirectoryName, first.DirectoryName], served);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task BaseReleaseMustPrecedeTheNewRelease(int baseNumber)
    {
        var draft = await CreateDraftAsync();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            publications.CommitAsync(Owner.Subject, Commit(2, baseNumber, draft.DraftId, 1, 0), "key", Hash(1), default));
    }

    [Fact]
    public async Task AStatementDecisionAfterTheBuildBlocksTheCommitBecauseApprovalMayHaveChanged()
    {
        var statement = await CreateApprovedStatementAsync();

        await Assert.ThrowsAsync<PublicationConflictException>(() =>
            publications.CommitAsync(Owner.Subject, StatementCommit(1, statement, reviewStateVersion: 0), "stale", Hash(1), default));
        Assert.Equal(new PublicationState(0, null), await publications.GetStateAsync(default));
        var committed = await publications.CommitAsync(Owner.Subject, StatementCommit(1, statement, reviewStateVersion: 1), "fresh", Hash(2), default);
        Assert.Equal(1, committed.ReleaseNumber);
    }

    [Fact]
    public async Task AStatementSavedAfterTheBuildBlocksTheCommitBecauseTheServedRevisionWouldBeStale()
    {
        var statement = await CreateApprovedStatementAsync();

        await Assert.ThrowsAsync<PublicationConflictException>(() => publications.CommitAsync(Owner.Subject,
            StatementCommit(1, statement, reviewStateVersion: 1, revision: 1), "stale", Hash(1), default));
    }

    [Fact]
    public async Task ADocumentChangeIsNeverCheckedAgainstTheStatementTableBecauseTheKindPicksTheDraftTable()
    {
        var draft = await CreateApprovedDraftAsync("a");

        await Assert.ThrowsAsync<PublicationConflictException>(() => publications.CommitAsync(Owner.Subject,
            new(1, null, $"000001-{Guid.NewGuid():N}", [new(PublishedRecordKind.Statement, draft.DraftId, 2)],
                [new(PublishedRecordKind.Statement, draft.DraftId, 2, 1)]), "wrong-kind", Hash(1), default));
    }

    [Fact]
    public async Task AStatementIsNeverCheckedAgainstTheActionTableBecauseTheKindPicksTheDraftTable()
    {
        var statement = await CreateApprovedStatementAsync();

        await Assert.ThrowsAsync<PublicationConflictException>(() => publications.CommitAsync(Owner.Subject,
            new(1, null, $"000001-{Guid.NewGuid():N}", [new(PublishedRecordKind.Action, statement, 2)],
                [new(PublishedRecordKind.Action, statement, 2, 1)]), "wrong-kind", Hash(1), default));
        Assert.Equal(1, (await publications.CommitAsync(Owner.Subject, StatementCommit(1, statement, reviewStateVersion: 1), "right-kind",
            Hash(2), default)).ReleaseNumber);
    }

    [Fact]
    public async Task CommitWaitsForTheStatementLockBecauseAStatementDecisionMustNotSlipInDuringThePublish()
    {
        var statement = await CreateApprovedStatementAsync();
        await using var holder = await HeldAdvisoryLock.AcquireAsync(connectionString, "civic-lens-statement-review");

        var committing = publications.CommitAsync(Owner.Subject, StatementCommit(1, statement, reviewStateVersion: 1), "key", Hash(1), default);
        await HeldAdvisoryLock.WaitForWaiterAsync(connectionString, "civic-lens-statement-review");
        Assert.False(committing.IsCompleted);
        await holder.DisposeAsync();

        Assert.Equal(1, (await committing).ReleaseNumber);
    }

    [Fact]
    public async Task CommitWaitsForTheActionLockBecauseAnActionDecisionMustNotSlipInDuringThePublish()
    {
        await using var holder = await HeldAdvisoryLock.AcquireAsync(connectionString, "civic-lens-action-review");

        var committing = publications.CommitAsync(Owner.Subject,
            new(1, null, $"000001-{Guid.NewGuid():N}", [], []), "key", Hash(1), default);
        await Task.Delay(400);
        Assert.False(committing.IsCompleted);
        await holder.DisposeAsync();

        Assert.Equal(1, (await committing).ReleaseNumber);
    }

    [Fact]
    public async Task ServingWaitsForTheDocumentChangeLockToo()
    {
        await using var holder = await HeldAdvisoryLock.AcquireAsync(connectionString, "civic-lens-document-change-review");

        var serving = publications.ServeActiveAsync((_, _) => Task.CompletedTask, default);
        await HeldAdvisoryLock.WaitForWaiterAsync(connectionString, "civic-lens-document-change-review");
        Assert.False(serving.IsCompleted);
        await holder.DisposeAsync();

        Assert.Null(await serving);
    }

    [Fact]
    public async Task ReadyStatementListFollowsTheActiveReleaseByKindSoAPublishedStatementLeavesTheList()
    {
        var neverPublished = await CreateApprovedStatementAsync();
        var published = await CreateApprovedStatementAsync();
        await publications.CommitAsync(Owner.Subject, StatementCommit(1, published, reviewStateVersion: 1), "key", Hash(1), default);

        var ready = await statementReviews.ListUnpublishedApprovedAsync(10, default);

        Assert.Equal([neverPublished], ready.Select(item => item.Statement.CandidateId));
        Assert.Null(ready[0].PublishedRevisionNumber);
    }

    [Fact]
    public async Task APublishedStatementWithAnIdShapedLikeADraftDoesNotHideTheDocumentChange()
    {
        var draft = await CreateApprovedDraftAsync("a");
        await InsertActiveReleaseAsync($$"""[{"kind":"statement","recordId":"{{draft.DraftId}}","revisionNumber":{{draft.RevisionNumber}}}]""");

        var ready = await reviews.ListUnpublishedApprovedDraftsAsync(10, default);

        var item = Assert.Single(ready);
        Assert.Equal(draft.DraftId, item.Draft.DraftId);
        Assert.Null(item.PublishedRevisionNumber);
    }

    [Fact]
    public async Task ReleaseRowWrittenBeforeKindsExistedStillCountsItsDraftsAsPublishedDocumentChanges()
    {
        var draft = await CreateApprovedDraftAsync("a");
        await InsertActiveReleaseAsync($$"""[{"draftId":"{{draft.DraftId}}","revisionNumber":{{draft.RevisionNumber}}}]""");

        Assert.Empty(await reviews.ListUnpublishedApprovedDraftsAsync(10, default));
    }

    [Fact]
    public async Task ReleaseRowWrittenNowCarriesTheKindOfEachRecordSoBothListsReadItBack()
    {
        var draft = await CreateApprovedDraftAsync("a");
        var statement = await CreateApprovedStatementAsync();
        await publications.CommitAsync(Owner.Subject, new(1, null, $"000001-{Guid.NewGuid():N}",
            [new(PublishedRecordKind.DocumentChange, draft.DraftId, draft.RevisionNumber), new(PublishedRecordKind.Statement, statement, 2)],
            [new(PublishedRecordKind.DocumentChange, draft.DraftId, draft.RevisionNumber, 1),
             new(PublishedRecordKind.Statement, statement, 2, 1)]), "key", Hash(1), default);

        Assert.Empty(await reviews.ListUnpublishedApprovedDraftsAsync(10, default));
        Assert.Empty(await statementReviews.ListUnpublishedApprovedAsync(10, default));
    }

    [Fact]
    public async Task PublicationReadReturnsTheApprovedStatementWithItsExtractionAndUrlAndOmitsUnknownIds()
    {
        var statement = await CreateApprovedStatementAsync();
        var unknown = new string('f', 64);

        var found = await statementReviews.GetForPublicationAsync([statement, unknown], default);

        var read = Assert.Single(found);
        Assert.Equal(statement, read.Key);
        Assert.Equal(ReviewStatus.Approved, read.Value.Review.CurrentStatus);
        Assert.StartsWith("https://example.test/pages/", read.Value.Url, StringComparison.Ordinal);
        Assert.Equal(read.Value.Review.CurrentRevision!.Quotations[0].Span.ExtractionId, read.Value.Extraction.ExtractionId);
    }

    [Fact]
    public async Task ReadyListFollowsTheActiveReleaseSoNewAndReplacementDraftsShowAndPublishedOnesDoNot()
    {
        var neverPublished = await CreateApprovedDraftAsync("a");
        var replaced = await CreateApprovedDraftAsync("b");
        var current = await CreateApprovedDraftAsync("c");
        var onlyInOldRelease = await CreateApprovedDraftAsync("d");
        var notApproved = await CreateDraftAsync("e");
        var withdrawn = await CreateApprovedDraftAsync("f");
        await Decide(withdrawn, 1, ReviewDecisionKind.WithdrawApproval, "Wrong source.");
        await CommitAsync(1, null, replaced, current, onlyInOldRelease);
        await CommitAsync(2, 1, replaced, current);
        var revised = await SaveRevisionAsync(replaced, "b2");
        await Decide(revised, 1, ReviewDecisionKind.Approve);

        var ready = await reviews.ListUnpublishedApprovedDraftsAsync(10, default);

        Assert.Equivalent(new Dictionary<string, int?>
        {
            [neverPublished.DraftId] = null,
            [replaced.DraftId] = replaced.RevisionNumber,
            [onlyInOldRelease.DraftId] = null
        }, ready.ToDictionary(item => item.Draft.DraftId, item => item.PublishedRevisionNumber));
        Assert.Equal(revised.RevisionNumber, ready.Single(item => item.Draft.DraftId == replaced.DraftId).Draft.CurrentRevision.RevisionNumber);
        Assert.DoesNotContain(ready, item => item.Draft.DraftId == current.DraftId || item.Draft.DraftId == notApproved.DraftId ||
            item.Draft.DraftId == withdrawn.DraftId);
        Assert.Single(await reviews.ListUnpublishedApprovedDraftsAsync(1, default));
    }

    private static PublicationCommit StatementCommit(int number, string candidateId, int reviewStateVersion, int revision = 2) =>
        new(number, number == 1 ? null : number - 1, $"{number:D6}-{Guid.NewGuid():N}",
            [new(PublishedRecordKind.Statement, candidateId, revision)],
            [new(PublishedRecordKind.Statement, candidateId, revision, reviewStateVersion)]);

    private async Task<string> CreateApprovedStatementAsync()
    {
        const string quote = "a rent cap for tenants";
        var candidate = await statementSeeder.SeedAsync($"{StatementDraftFixtures.Article} Release {++statementCount}.",
            $"https://example.test/pages/{statementCount}");
        var reader = new StatementReviewContextReader(statementDrafts, new StatementSelectionResolver(StatementGateFixtures.Configuration()));
        await new CreateStatementDraft(statementReviews).ExecuteAsync(Owner, new(candidate, "create-" + candidate[..8]));
        var extraction = (await statementDrafts.ReadEvidenceAsync(candidate, default))!.Article.Extraction;
        var span = new SourceSpan(extraction.ExtractionId, extraction.Text.IndexOf(quote, StringComparison.Ordinal), quote.Length);
        await new SaveStatementDraft(statementReviews, reader).ExecuteAsync(Owner, new(candidate, 1, "Warren announces a rent cap",
            "The senator announced a rent cap for tenants.", null, null, [StatementDraftFixtures.Warren], [],
            [StatementGateFixtures.RentIssue], [StatementGateFixtures.HousingArea], [span], "save-" + candidate[..8]));
        await new DecideStatementReview(statementReviews, reader).ExecuteAsync(Owner,
            new(candidate, 2, 0, ReviewDecisionKind.Approve, null, [], "approve-" + candidate[..8]));
        return candidate;
    }

    private async Task<long> DatabaseTicksAsync() => await ScalarTicksAsync(
        "SELECT (EXTRACT(EPOCH FROM clock_timestamp())::numeric * 10000000 + 621355968000000000)::bigint");

    private async Task<long> StoredPublishedTicksAsync() => await ScalarTicksAsync("SELECT published_at_utc_ticks FROM publication_releases");

    private async Task<long> ScalarTicksAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private async Task InsertActiveReleaseAsync(string recordsJson)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            INSERT INTO publication_releases (release_number, directory_name, actor_subject, published_at_utc_ticks, record_count, records_json, is_active)
            VALUES (1, @directory, 'auth0|owner', @ticks, 1, @records, true)
            """, connection);
        command.Parameters.AddWithValue("directory", $"000001-{Guid.NewGuid():N}");
        command.Parameters.AddWithValue("ticks", PublishedAt.UtcTicks);
        command.Parameters.AddWithValue("records", recordsJson);
        await command.ExecuteNonQueryAsync();
    }

    private static PublicationCommit Commit(int number, string draftId, int revision, int reviewStateVersion) =>
        Commit(number, number == 1 ? null : number - 1, draftId, revision, reviewStateVersion);

    private static PublicationCommit Commit(int number, int? baseNumber, string draftId, int revision,
        int reviewStateVersion) => new(number, baseNumber, $"{number:D6}-{Guid.NewGuid():N}",
        [new(PublishedRecordKind.DocumentChange, draftId, revision)],
        [new(PublishedRecordKind.DocumentChange, draftId, revision, reviewStateVersion)]);

    private static PublicationCommit Commit(int number, DocumentChangeDraftRevision draft, int revision, int reviewStateVersion) =>
        Commit(number, draft.DraftId, revision, reviewStateVersion);

    private static string Hash(int seed) => Convert.ToHexStringLower(SHA256.HashData(BitConverter.GetBytes(seed)));

    private static async Task<object> ObserveAsync(Func<Task<PublicationReleaseSummary>> action)
    {
        try { return await action(); }
        catch (PublicationConflictException exception) { return exception; }
    }

    private async Task<DocumentChangeDraftRevision> CreateDraftAsync(string name)
    {
        var comparison = await seeder.SaveAsync($"{name} before.", $"{name} after.");
        return await new CreateDocumentChangeDraft(reviews).ExecuteAsync(Owner, new(comparison.ComparisonId, $"create-{name}"));
    }

    private async Task<DocumentChangeDraftRevision> CreateApprovedDraftAsync(string name)
    {
        var saved = await SaveRevisionAsync(await CreateDraftAsync(name), name + "1");
        await Decide(saved, 0, ReviewDecisionKind.Approve);
        return saved;
    }

    private async Task<DocumentChangeDraftRevision> SaveRevisionAsync(DocumentChangeDraftRevision draft, string name) =>
        await new SaveDocumentChangeDraft(reviews, new ReviewCatalog([], [])).ExecuteAsync(Owner,
            new(draft.DraftId, draft.RevisionNumber, $"Headline {name}", "The policy changed.", null, null, "Example Office",
                null, null, [], [], draft.Citations, $"save-{name}"));

    private Task<ReviewDecision> Decide(DocumentChangeDraftRevision draft, int stateVersion, ReviewDecisionKind kind,
        string? note = null) =>
        new DecideDocumentChangeReview(reviews, new ReviewCatalog([], [])).ExecuteAsync(Owner,
            new(draft.DraftId, draft.RevisionNumber, stateVersion, kind, note, [],
                $"decide-{draft.DraftId}-{draft.RevisionNumber}-{stateVersion}"));

    private async Task CommitAsync(int number, int? baseNumber, params DocumentChangeDraftRevision[] drafts)
    {
        var records = drafts.Select(draft => new PublishedRevisionBinding(PublishedRecordKind.DocumentChange, draft.DraftId, draft.RevisionNumber)).ToImmutableArray();
        var expectations = drafts.Select(draft => new ReviewStateExpectation(PublishedRecordKind.DocumentChange, draft.DraftId, draft.RevisionNumber, 1)).ToImmutableArray();
        await publications.CommitAsync(Owner.Subject, new(number, baseNumber, $"{number:D6}-{Guid.NewGuid():N}",
            records, expectations), $"commit-{number}", Hash(number), default);
    }

    private async Task<DocumentChangeDraftRevision> CreateDraftAsync()
    {
        var comparison = await seeder.SaveAsync("Policy before.", "Policy after.");
        return await new CreateDocumentChangeDraft(reviews).ExecuteAsync(Owner, new(comparison.ComparisonId, "create-key"));
    }

    private Task<DocumentChangeDraftRevision> SaveRevisionAsync(DocumentChangeDraftRevision draft) =>
        new SaveDocumentChangeDraft(reviews, new ReviewCatalog([], [])).ExecuteAsync(Owner,
            new(draft.DraftId, 1, "Policy update", "The policy changed.", null, null, "Example Office",
                null, null, [], [], draft.Citations, "save-key"));
}
