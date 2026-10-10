using System.Collections.Immutable;
using System.Security.Cryptography;
using CivicLens.Application.Publication;
using CivicLens.Application.Review;
using CivicLens.Core.Review;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Documents;
using CivicLens.Infrastructure.Publication;
using CivicLens.Infrastructure.Review;
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
    private readonly string schema = "publication_" + Guid.NewGuid().ToString("N");
    private readonly string captureRoot = Path.Combine(Path.GetTempPath(), "civic-publication-" + Guid.NewGuid().ToString("N"));
    private PooledDbContextFactory<CollectionAttemptDbContext> factory = null!;
    private PostgresCollectionAttemptStore attempts = null!;
    private PostgresDocumentExtractionStore extractions = null!;
    private PostgresDocumentComparisonStore comparisons = null!;
    private PostgresDocumentChangeReviewStore reviews = null!;
    private PostgresPublicationStore publications = null!;
    private ComparisonSeeder seeder = null!;

    public async Task InitializeAsync()
    {
        await using (var connection = new NpgsqlConnection(postgres.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"CREATE SCHEMA {schema}";
            await command.ExecuteNonQueryAsync();
        }

        var connectionString = new NpgsqlConnectionStringBuilder(postgres.ConnectionString) { SearchPath = schema, Pooling = false }.ConnectionString;
        factory = new PooledDbContextFactory<CollectionAttemptDbContext>(
            new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString).Options);
        attempts = new PostgresCollectionAttemptStore(factory);
        extractions = new PostgresDocumentExtractionStore(factory);
        comparisons = new PostgresDocumentComparisonStore(factory);
        reviews = new PostgresDocumentChangeReviewStore(factory);
        publications = new PostgresPublicationStore(factory);
        seeder = new ComparisonSeeder(attempts, extractions, comparisons, captureRoot);
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
    public async Task MigrationMatchesTheModelSoReleasesCannotDriftFromTheSchema()
    {
        await using var db = await factory.CreateDbContextAsync();

        Assert.Contains("20261009120000_PublicationReleases", await db.Database.GetAppliedMigrationsAsync());
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.False(db.Database.HasPendingModelChanges());
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
        Assert.Equal(PublishedAt, second.PublishedAtUtc);
        Assert.Equal(1, second.RecordCount);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => publications.ListAsync(0, default));
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
            await Task.Delay(TimeSpan.FromMilliseconds(300));
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

    private static PublicationCommit Commit(int number, string draftId, int revision, int reviewStateVersion) =>
        Commit(number, number == 1 ? null : number - 1, draftId, revision, reviewStateVersion);

    private static PublicationCommit Commit(int number, int? baseNumber, string draftId, int revision,
        int reviewStateVersion) => new(number, baseNumber, $"{number:D6}-{Guid.NewGuid():N}", PublishedAt,
        [new(draftId, revision)], [new(draftId, revision, reviewStateVersion)]);

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
        var records = drafts.Select(draft => new PublishedRevisionBinding(draft.DraftId, draft.RevisionNumber)).ToImmutableArray();
        var expectations = drafts.Select(draft => new ReviewStateExpectation(draft.DraftId, draft.RevisionNumber, 1)).ToImmutableArray();
        await publications.CommitAsync(Owner.Subject, new(number, baseNumber, $"{number:D6}-{Guid.NewGuid():N}", PublishedAt,
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
