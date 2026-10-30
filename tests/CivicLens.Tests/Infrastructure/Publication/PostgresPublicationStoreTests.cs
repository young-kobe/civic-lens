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
    private const string DatabaseClockTicksSql =
        "((EXTRACT(EPOCH FROM clock_timestamp())::numeric * 10000000 + 621355968000000000)::bigint)";
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
    public async Task TheModelAuthorCannotCommitARelease()
    {
        var draft = await CreateDraftAsync();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            publications.CommitAsync(ReviewAuthor.AnalysisSubject, Commit(1, draft, 1, 0), "key", Hash(1), default));
        Assert.Equal(new PublicationState(0, null), await publications.GetStateAsync(default));
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

        Assert.Equal(first, await publications.ActivateAsync(1, (_, _) => Task.CompletedTask, default));
        Assert.Equal(new PublicationState(2, first), await publications.GetStateAsync(default));
        await Assert.ThrowsAsync<ArgumentException>(() => publications.ActivateAsync(9, (_, _) => Task.CompletedTask, default));
        Assert.Equal(first, (await publications.GetStateAsync(default)).Active);
    }

    [Fact]
    public async Task ActivationRefusedForMissingFilesLeavesThePreviousActiveReleaseUnchangedBecauseTheLinkStillServesIt()
    {
        var draft = await CreateDraftAsync();
        var first = await publications.CommitAsync(Owner.Subject, Commit(1, draft, 1, 0), "key-1", Hash(1), default);
        var second = await publications.CommitAsync(Owner.Subject, Commit(2, draft, 1, 0), "key-2", Hash(2), default);
        string? checkedDirectory = null;

        await Assert.ThrowsAsync<FileNotFoundException>(() => publications.ActivateAsync(1, (directory, _) =>
        {
            checkedDirectory = directory;
            throw new FileNotFoundException();
        }, default));

        Assert.Equal(first.DirectoryName, checkedDirectory);
        Assert.Equal(second, (await publications.GetStateAsync(default)).Active);
    }

    [Fact]
    public async Task ActivationChecksTheFilesWhileHoldingThePublicationLockSoAPruneCannotDeleteThemBetweenCheckAndSwitch()
    {
        var draft = await CreateDraftAsync();
        await publications.CommitAsync(Owner.Subject, Commit(1, draft, 1, 0), "key-1", Hash(1), default);
        await publications.CommitAsync(Owner.Subject, Commit(2, draft, 1, 0), "key-2", Hash(2), default);
        var lockWasFree = true;

        await publications.ActivateAsync(1, async (_, _) =>
        {
            await using var other = new NpgsqlConnection(connectionString);
            await other.OpenAsync();
            await using var transaction = await other.BeginTransactionAsync();
            await using var command = new NpgsqlCommand(
                "SELECT pg_try_advisory_xact_lock(hashtextextended('civic-lens-document-change-review', 0))", other, transaction);
            lockWasFree = (bool)(await command.ExecuteScalarAsync())!;
        }, default);

        Assert.False(lockWasFree);
    }

    [Fact]
    public async Task PruneRetiresBelowTheLowestOfTheKeptNumbersAndNamesTheActiveReleaseSoARolledBackReleaseSurvives()
    {
        var draft = await CreateDraftAsync();
        var first = await publications.CommitAsync(Owner.Subject, Commit(1, draft, 1, 0), "key-1", Hash(1), default);
        await publications.CommitAsync(Owner.Subject, Commit(2, draft, 1, 0), "key-2", Hash(2), default);
        await publications.CommitAsync(Owner.Subject, Commit(3, draft, 1, 0), "key-3", Hash(3), default);
        await publications.ActivateAsync(1, (_, _) => Task.CompletedTask, default);
        (int Below, string? Active)? offered = null;

        await publications.PruneAsync(2, TimeSpan.Zero, (below, active, _) => { offered = (below, active); return Task.CompletedTask; }, default);

        Assert.Equal((2, first.DirectoryName), offered);
        Assert.Equal(3, (await publications.ListAsync(10, default)).Count);
    }

    [Fact]
    public async Task PruneKeepsEveryReleaseYoungerThanTheMinimumAgeEvenPastTheCountSoACachedPageOrSearchPollCanStillReadIt()
    {
        var draft = await CreateDraftAsync();
        for (var number = 1; number <= 4; number++)
            await publications.CommitAsync(Owner.Subject, Commit(number, draft, 1, 0), $"key-{number}", Hash(number), default);
        await AgeReleaseAsync(1, PruneReleases.MinimumAge + TimeSpan.FromMinutes(1));
        await AgeReleaseAsync(2, PruneReleases.MinimumAge + TimeSpan.FromMinutes(1));
        await AgeReleaseAsync(3, PruneReleases.MinimumAge - TimeSpan.FromMinutes(1));

        var below = await LowestKeptAsync(keep: 1);

        Assert.Equal(3, below);
    }

    [Fact]
    public async Task PruneRetiresAnOldReleaseThatIsPastTheCountSoDiskUseStaysBounded()
    {
        var draft = await CreateDraftAsync();
        for (var number = 1; number <= 3; number++)
            await publications.CommitAsync(Owner.Subject, Commit(number, draft, 1, 0), $"key-{number}", Hash(number), default);
        for (var number = 1; number <= 3; number++)
            await AgeReleaseAsync(number, PruneReleases.MinimumAge + TimeSpan.FromMinutes(1));

        var below = await LowestKeptAsync(keep: 2);

        Assert.Equal(2, below);
    }

    [Fact]
    public async Task PruneNamesTheActiveReleaseEvenWhenItIsOldSoARollbackTargetIsNeverRetired()
    {
        var draft = await CreateDraftAsync();
        var first = await publications.CommitAsync(Owner.Subject, Commit(1, draft, 1, 0), "key-1", Hash(1), default);
        await publications.CommitAsync(Owner.Subject, Commit(2, draft, 1, 0), "key-2", Hash(2), default);
        await publications.ActivateAsync(1, (_, _) => Task.CompletedTask, default);
        await AgeReleaseAsync(1, PruneReleases.MinimumAge + TimeSpan.FromDays(30));
        await AgeReleaseAsync(2, PruneReleases.MinimumAge + TimeSpan.FromDays(30));
        string? active = null;

        await publications.PruneAsync(1, PruneReleases.MinimumAge, (_, name, _) => { active = name; return Task.CompletedTask; }, default);

        Assert.Equal(first.DirectoryName, active);
    }

    private async Task<int> LowestKeptAsync(int keep)
    {
        var below = 0;
        await publications.PruneAsync(keep, PruneReleases.MinimumAge, (lowest, _, _) => { below = lowest; return Task.CompletedTask; }, default);
        return below;
    }

    private async Task AgeReleaseAsync(int number, TimeSpan age)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            $"UPDATE publication_releases SET published_at_utc_ticks = {DatabaseClockTicksSql} - @age WHERE release_number = @number", connection);
        command.Parameters.AddWithValue("age", age.Ticks);
        command.Parameters.AddWithValue("number", number);
        await command.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task PruneKeepsTheThirtyHighestNumbersAtTheRealLimit()
    {
        var draft = await CreateDraftAsync();
        for (var number = 1; number <= PruneReleases.KeepReleases + 1; number++)
            await publications.CommitAsync(Owner.Subject, Commit(number, draft, 1, 0), $"key-{number}", Hash(number), default);
        var below = 0;

        await publications.PruneAsync(PruneReleases.KeepReleases, TimeSpan.Zero, (lowestKept, _, _) => { below = lowestKept; return Task.CompletedTask; }, default);

        Assert.Equal(2, below);
    }

    [Fact]
    public async Task PruneRetiresFoldersWhileHoldingThePublicationLockSoNoActivationCanSwitchToAFolderBeingMoved()
    {
        var draft = await CreateDraftAsync();
        await publications.CommitAsync(Owner.Subject, Commit(1, draft, 1, 0), "key-1", Hash(1), default);
        await publications.CommitAsync(Owner.Subject, Commit(2, draft, 1, 0), "key-2", Hash(2), default);
        var lockWasFree = true;

        await publications.PruneAsync(1, TimeSpan.Zero, async (_, _, _) =>
        {
            await using var other = new NpgsqlConnection(connectionString);
            await other.OpenAsync();
            await using var transaction = await other.BeginTransactionAsync();
            await using var command = new NpgsqlCommand(
                "SELECT pg_try_advisory_xact_lock(hashtextextended('civic-lens-brief-review', 0))", other, transaction);
            lockWasFree = (bool)(await command.ExecuteScalarAsync())!;
        }, default);

        Assert.False(lockWasFree);
    }

    [Fact]
    public async Task ReleaseBuiltBeforeARollbackIsRejectedSoItCannotRestoreWithdrawnRecords()
    {
        var draft = await CreateDraftAsync();
        await publications.CommitAsync(Owner.Subject, Commit(1, draft, 1, 0), "key-1", Hash(1), default);
        await publications.CommitAsync(Owner.Subject, Commit(2, draft, 1, 0), "key-2", Hash(2), default);
        var rolledBack = await publications.ActivateAsync(1, (_, _) => Task.CompletedTask, default);

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
            rollback = publications.ActivateAsync(1, (_, _) => Task.CompletedTask, default);
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
    public async Task CommitWaitsForTheVoteLockBecauseAVoteDecisionMustNotSlipInDuringThePublish()
    {
        await using var holder = await HeldAdvisoryLock.AcquireAsync(connectionString, "civic-lens-vote-review");

        var committing = publications.CommitAsync(Owner.Subject,
            new(1, null, $"000001-{Guid.NewGuid():N}", [], []), "key", Hash(1), default);
        await Task.Delay(400);
        Assert.False(committing.IsCompleted);
        await holder.DisposeAsync();

        Assert.Equal(1, (await committing).ReleaseNumber);
    }

    [Fact]
    public async Task CommitWaitsForTheRelationshipLockBecauseARelationshipDecisionMustNotSlipInDuringThePublish()
    {
        await using var holder = await HeldAdvisoryLock.AcquireAsync(connectionString, "civic-lens-relationship-review");

        var committing = publications.CommitAsync(Owner.Subject,
            new(1, null, $"000001-{Guid.NewGuid():N}", [], []), "key", Hash(1), default);
        await HeldAdvisoryLock.WaitForWaiterAsync(connectionString, "civic-lens-relationship-review");
        Assert.False(committing.IsCompleted);
        await holder.DisposeAsync();

        Assert.Equal(1, (await committing).ReleaseNumber);
    }

    [Fact]
    public async Task TheCommitHoldsTheVoteLockWhileItWaitsForTheRelationshipLockSoTheRelationshipLockComesAfterTheRecordLocks()
    {
        await using var holder = await HeldAdvisoryLock.AcquireAsync(connectionString, "civic-lens-relationship-review");
        var committing = publications.CommitAsync(Owner.Subject,
            new(1, null, $"000001-{Guid.NewGuid():N}", [], []), "key", Hash(1), default);
        await HeldAdvisoryLock.WaitForWaiterAsync(connectionString, "civic-lens-relationship-review");

        var acquiring = HeldAdvisoryLock.AcquireAsync(connectionString, "civic-lens-vote-review");
        await Task.Delay(400);
        Assert.False(acquiring.IsCompleted);
        await holder.DisposeAsync();

        Assert.Equal(1, (await committing).ReleaseNumber);
        await (await acquiring).DisposeAsync();
    }

    [Fact]
    public async Task ServingWaitsForTheRelationshipLockToo()
    {
        await using var holder = await HeldAdvisoryLock.AcquireAsync(connectionString, "civic-lens-relationship-review");

        var serving = publications.ServeActiveAsync((_, _) => Task.CompletedTask, default);
        await HeldAdvisoryLock.WaitForWaiterAsync(connectionString, "civic-lens-relationship-review");
        Assert.False(serving.IsCompleted);
        await holder.DisposeAsync();

        Assert.Null(await serving);
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

        var ready = await statementReviews.ListUnpublishedApprovedAsync(10, [], default);

        Assert.Equal([neverPublished], ready.Select(item => item.Statement.CandidateId));
        Assert.Null(ready[0].PublishedRevisionNumber);
    }

    [Fact]
    public async Task APublishedStatementWithAnIdShapedLikeADraftDoesNotHideTheDocumentChange()
    {
        var draft = await CreateApprovedDraftAsync("a");
        await InsertActiveReleaseAsync($$"""[{"kind":"statement","recordId":"{{draft.DraftId}}","revisionNumber":{{draft.RevisionNumber}}}]""");

        var ready = await reviews.ListUnpublishedApprovedDraftsAsync(10, [], default);

        var item = Assert.Single(ready);
        Assert.Equal(draft.DraftId, item.Draft.DraftId);
        Assert.Null(item.PublishedRevisionNumber);
    }

    [Fact]
    public async Task ReleaseRowWrittenBeforeKindsExistedStillCountsItsDraftsAsPublishedDocumentChanges()
    {
        var draft = await CreateApprovedDraftAsync("a");
        await InsertActiveReleaseAsync($$"""[{"draftId":"{{draft.DraftId}}","revisionNumber":{{draft.RevisionNumber}}}]""");

        Assert.Empty(await reviews.ListUnpublishedApprovedDraftsAsync(10, [], default));
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

        Assert.Empty(await reviews.ListUnpublishedApprovedDraftsAsync(10, [], default));
        Assert.Empty(await statementReviews.ListUnpublishedApprovedAsync(10, [], default));
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

        var ready = await reviews.ListUnpublishedApprovedDraftsAsync(10, [], default);

        Assert.Equivalent(new Dictionary<string, int?>
        {
            [neverPublished.DraftId] = null,
            [replaced.DraftId] = replaced.RevisionNumber,
            [onlyInOldRelease.DraftId] = null
        }, ready.ToDictionary(item => item.Draft.DraftId, item => item.PublishedRevisionNumber));
        Assert.Equal(revised.RevisionNumber, ready.Single(item => item.Draft.DraftId == replaced.DraftId).Draft.CurrentRevision.RevisionNumber);
        Assert.DoesNotContain(ready, item => item.Draft.DraftId == current.DraftId || item.Draft.DraftId == notApproved.DraftId ||
            item.Draft.DraftId == withdrawn.DraftId);
        Assert.Single(await reviews.ListUnpublishedApprovedDraftsAsync(1, [], default));
    }

    public static TheoryData<PublishedRecordKind> AllKinds => new(Enum.GetValues<PublishedRecordKind>());

    [Theory]
    [MemberData(nameof(AllKinds))]
    public async Task OnlyARecordThatReviewBlockedCanLeaveTheReleaseForEveryKind(PublishedRecordKind kind)
    {
        var recordId = await SeedDraftAsync(kind);
        var number = 0;
        foreach (var status in new[] { ReviewStatus.AwaitingReview, ReviewStatus.Approved })
        {
            await SetReviewStatusAsync(kind, recordId, status);
            var error = await Assert.ThrowsAsync<ArgumentException>(() =>
                publications.CommitAsync(Owner.Subject, WithdrawalCommit(1, null, kind, recordId), "key", Hash(1), default));
            Assert.Contains($"is {status} in review", error.Message, StringComparison.Ordinal);
        }
        foreach (var status in new[] { ReviewStatus.ApprovalWithdrawn, ReviewStatus.ChangesRequested, ReviewStatus.AwaitingReviewWithConcerns })
        {
            await SetReviewStatusAsync(kind, recordId, status);
            number++;
            var summary = await publications.CommitAsync(Owner.Subject,
                WithdrawalCommit(number, number == 1 ? null : number - 1, kind, recordId), $"key-{number}", Hash(number), default);
            Assert.Equal(number, summary.ReleaseNumber);
        }
    }

    [Theory]
    [MemberData(nameof(AllKinds))]
    public async Task TheStatusReadReportsTheCurrentReviewStatusForEveryKindAndSkipsARecordWithNoReviewRow(PublishedRecordKind kind)
    {
        var recordId = await SeedDraftAsync(kind);
        var reference = new PublishedRecordReference(kind, recordId);
        var missing = new PublishedRecordReference(kind, new string('9', 64));
        foreach (var status in new[] { ReviewStatus.Approved, ReviewStatus.ChangesRequested })
        {
            await SetReviewStatusAsync(kind, recordId, status);

            var statuses = await publications.ReadReviewStatusesAsync([reference, missing], default);

            Assert.Equal(status, Assert.Single(statuses).Value);
            Assert.Equal(reference, Assert.Single(statuses).Key);
        }
    }

    [Theory]
    [MemberData(nameof(StatusesAPublishedKindCanHold))]
    public async Task TheBlockedListAndTheWithdrawalCommitAgreeOnEveryReviewStatus(ReviewStatus status)
    {
        const PublishedRecordKind kind = PublishedRecordKind.Action;
        var recordId = await SeedDraftAsync(kind);
        await SetReviewStatusAsync(kind, recordId, status);
        var entry = new PublishedRecordEntry
        {
            Officials = [],
            IssueIds = [],
            PolicyAreaIds = [],
            Kind = kind,
            RecordId = recordId,
            RevisionNumber = 1,
            Headline = "Headline",
            FirstPublishedAtUtc = PublishedAt
        };

        var listed = (await new BlockedPublishedRecords(publications).FindAsync([entry], default)).Count == 1;
        var failure = await Record.ExceptionAsync(() => publications.CommitAsync(Owner.Subject,
            WithdrawalCommit(1, null, kind, recordId), "key", Hash(1), default));

        Assert.Equal(PublishedRecordRules.IsWithdrawable(status), listed);
        Assert.Equal(listed, failure is null);
        if (failure is not null)
            Assert.Contains($"is {status} in review", Assert.IsType<ArgumentException>(failure).Message, StringComparison.Ordinal);
    }

    public static TheoryData<ReviewStatus> StatusesAPublishedKindCanHold => new(Enum.GetValues<ReviewStatus>().Where(status => status != ReviewStatus.NotDrafted));

    [Fact]
    public async Task AReviewChangeBetweenTheBuildAndTheCommitOfAReturnFailsTheCommit()
    {
        var draft = await CreateApprovedDraftAsync("r");
        await CommitAsync(1, null, draft);
        await Decide(draft, 1, ReviewDecisionKind.WithdrawApproval, "Wrong source.");
        await publications.CommitAsync(Owner.Subject, WithdrawalCommit(2, 1, PublishedRecordKind.DocumentChange, draft.DraftId), "withdraw", Hash(2), default);
        var revised = await SaveRevisionAsync(draft, "r2");
        var builtBeforeTheChange = ReturnCommit(3, revised, 2);
        await Decide(revised, 2, ReviewDecisionKind.RequestChanges, "Please explain the context.");

        await Assert.ThrowsAsync<PublicationConflictException>(() =>
            publications.CommitAsync(Owner.Subject, builtBeforeTheChange, "return", Hash(3), default));
        var committed = await publications.CommitAsync(Owner.Subject, ReturnCommit(3, revised, 3), "return-2", Hash(4), default);
        Assert.Equal(3, committed.ReleaseNumber);
    }

    [Fact]
    public async Task AWithdrawalOfARecordWithNoReviewRowIsRefused()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => publications.CommitAsync(Owner.Subject,
            WithdrawalCommit(1, null, PublishedRecordKind.Action, new string('9', 64)), "key", Hash(1), default));
    }

    private static PublicationCommit ReturnCommit(int number, DocumentChangeDraftRevision draft, int reviewStateVersion) =>
        new(number, number - 1, $"{number:D6}-{Guid.NewGuid():N}",
            [new(PublishedRecordKind.DocumentChange, draft.DraftId, draft.RevisionNumber)],
            [new(PublishedRecordKind.DocumentChange, draft.DraftId, draft.RevisionNumber, reviewStateVersion)]);

    private static PublicationCommit WithdrawalCommit(int number, int? baseNumber, PublishedRecordKind kind, string recordId) =>
        new(number, baseNumber, $"{number:D6}-{Guid.NewGuid():N}", [], [])
        {
            WithdrawnRecords = [new(kind, recordId)]
        };

    private async Task<string> SeedDraftAsync(PublishedRecordKind kind)
    {
        var id = new string('a', 64);
        switch (kind)
        {
            case PublishedRecordKind.DocumentChange:
                return (await CreateApprovedDraftAsync("a")).DraftId;
            case PublishedRecordKind.Statement:
                return await CreateApprovedStatementAsync();
            case PublishedRecordKind.Action:
                await ExecuteAsync("""
                    INSERT INTO action_drafts (record_id, source_id, congress, bill_type, bill_number, current_revision_number, created_at_utc_ticks, last_revised_at_utc_ticks, review_state_version, review_status)
                    VALUES (@id, 'congress-bills', 119, 'hr', 7, 1, 0, 0, 1, 'Approved')
                    """, id);
                break;
            case PublishedRecordKind.Vote:
                await ExecuteAsync("""
                    INSERT INTO vote_drafts (record_id, source_id, congress, session_number, roll_call_number, current_revision_number, review_state_version, review_status, created_at_utc_ticks, last_revised_at_utc_ticks, chamber)
                    VALUES (@id, 'congress-house-votes', 119, 1, 100, 1, 1, 'Approved', 0, 0, 'House')
                    """, id);
                break;
            case PublishedRecordKind.Relationship:
                await ExecuteAsync("""
                    INSERT INTO relationship_drafts (record_id, current_revision_number, review_state_version, review_status, no_longer_matches, created_at_utc_ticks, last_revised_at_utc_ticks)
                    VALUES (@id, 1, 1, 'Approved', false, 0, 0)
                    """, id);
                break;
            case PublishedRecordKind.Brief:
                await ExecuteAsync("""
                    INSERT INTO brief_drafts (record_id, week_start, current_revision_number, review_state_version, review_status, created_at_utc_ticks, last_revised_at_utc_ticks)
                    VALUES (@id, '2026-01-05', 1, 1, 'Approved', 0, 0)
                    """, id);
                break;
            case PublishedRecordKind.ExecutiveAction:
                await ExecuteAsync("""
                    INSERT INTO executive_action_drafts (record_id, current_revision_number, review_state_version, review_status, created_at_utc_ticks, last_revised_at_utc_ticks)
                    VALUES (@id, 1, 1, 'Approved', 0, 0)
                    """, id);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown record kind.");
        }
        return id;
    }

    private Task SetReviewStatusAsync(PublishedRecordKind kind, string recordId, ReviewStatus status)
    {
        var (table, column) = kind switch
        {
            PublishedRecordKind.DocumentChange => ("document_change_drafts", "draft_id"),
            PublishedRecordKind.Statement => ("statement_drafts", "candidate_id"),
            PublishedRecordKind.Action => ("action_drafts", "record_id"),
            PublishedRecordKind.Vote => ("vote_drafts", "record_id"),
            PublishedRecordKind.Relationship => ("relationship_drafts", "record_id"),
            PublishedRecordKind.Brief => ("brief_drafts", "record_id"),
            PublishedRecordKind.ExecutiveAction => ("executive_action_drafts", "record_id"),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown record kind.")
        };
        return ExecuteAsync($"UPDATE {table} SET review_status = '{status}' WHERE {column} = @id", recordId);
    }

    private async Task ExecuteAsync(string sql, string id)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", id);
        await command.ExecuteNonQueryAsync();
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
