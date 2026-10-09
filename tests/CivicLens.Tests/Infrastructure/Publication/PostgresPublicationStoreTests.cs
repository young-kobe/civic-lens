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
        Assert.Null(await publications.GetLatestAsync(default));
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
        Assert.Equal(3, (await publications.GetLatestAsync(default))!.ReleaseNumber);
        var second = await publications.GetAsync(2, default);
        Assert.Equal(PublishedAt, second!.PublishedAtUtc);
        Assert.Equal(1, second.RecordCount);
        Assert.Null(await publications.GetAsync(9, default));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => publications.ListAsync(0, default));
    }

    private static PublicationCommit Commit(int number, string draftId, int revision, int reviewStateVersion) => new(number,
        $"{number:D6}-{Guid.NewGuid():N}", PublishedAt, [new(draftId, revision)], [new(draftId, revision, reviewStateVersion)]);

    private static PublicationCommit Commit(int number, DocumentChangeDraftRevision draft, int revision, int reviewStateVersion) =>
        Commit(number, draft.DraftId, revision, reviewStateVersion);

    private static string Hash(int seed) => Convert.ToHexStringLower(SHA256.HashData(BitConverter.GetBytes(seed)));

    private static async Task<object> ObserveAsync(Func<Task<PublicationReleaseSummary>> action)
    {
        try { return await action(); }
        catch (PublicationConflictException exception) { return exception; }
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
