using System.Security.Cryptography;
using System.Text;
using CivicLens.Application.Briefs;
using CivicLens.Application.Publication;
using CivicLens.Core.Briefs;
using CivicLens.Core.Review;
using CivicLens.Infrastructure.Briefs;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Publication;
using CivicLens.Publication.Contracts;
using CivicLens.Tests.Fixtures;
using CivicLens.Tests.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using static CivicLens.Tests.Fixtures.BriefWorldSeeder;

namespace CivicLens.Tests.Infrastructure.Briefs;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class BriefStalenessTests(PostgresCollection postgres) : IAsyncLifetime
{
    private static readonly DateOnly Week = BriefFixtures.Week;
    private static readonly string S1 = StatementId('1');
    private static readonly string S2 = StatementId('2');
    private static readonly string D1 = DocumentChangeId('d');
    private static readonly string A1 = ActionId('a');
    private static readonly string P1 = StatementId('p');
    private static readonly DateTimeOffset InPriorWeek = new(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset InWeek = BriefFixtures.InWeek;
    private static readonly DateOnly PriorWeek = Week.AddDays(-7);

    private string connectionString = null!;
    private PostgresPublicationStore publications = null!;
    private PostgresBriefDraftStore briefs = null!;
    private BriefWorldSeeder seeder = null!;

    public async Task InitializeAsync()
    {
        connectionString = await postgres.CreateMigratedDatabaseAsync();
        var factory = new PooledDbContextFactory<CollectionAttemptDbContext>(
            new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString).Options);
        publications = new PostgresPublicationStore(factory);
        briefs = new PostgresBriefDraftStore(factory);
        seeder = new BriefWorldSeeder(connectionString);
        await seeder.ReleaseAsync(1, InPriorWeek, active: false, Binding(PublishedRecordKind.Statement, P1, 1));
        await seeder.ReleaseAsync(2, InWeek, active: true, Binding(PublishedRecordKind.Statement, P1, 1),
            Binding(PublishedRecordKind.Statement, S1, 1), Binding(PublishedRecordKind.Statement, S2, 1),
            Binding(PublishedRecordKind.DocumentChange, D1, 1), Binding(PublishedRecordKind.Action, A1, 1));
    }

    public Task DisposeAsync() => postgres.DropDatabaseAsync(connectionString);

    [Fact]
    public async Task APublishThatChangesARevisionRequeuesOnlyTheBriefOfTheWeekThatFirstPublishedTheRecord()
    {
        await seeder.BriefAsync(Week);
        await seeder.BriefAsync(PriorWeek);

        await CommitAsync(Binding(PublishedRecordKind.Statement, P1, 1), Binding(PublishedRecordKind.Statement, S1, 1),
            Binding(PublishedRecordKind.Statement, S2, 2), Binding(PublishedRecordKind.DocumentChange, D1, 1), Binding(PublishedRecordKind.Action, A1, 1));

        Assert.Equal(("Pending:0", "Succeeded:3"), (await seeder.BriefQueueStatusAsync(Week), await seeder.BriefQueueStatusAsync(PriorWeek)));
    }

    [Fact]
    public async Task APublishThatChangesARecordOfAnEarlierWeekLeavesTheBriefOfThisWeekAlone()
    {
        await seeder.BriefAsync(Week);
        await seeder.BriefAsync(PriorWeek);

        await CommitAsync(Binding(PublishedRecordKind.Statement, P1, 2), Binding(PublishedRecordKind.Statement, S1, 1),
            Binding(PublishedRecordKind.Statement, S2, 1), Binding(PublishedRecordKind.DocumentChange, D1, 1), Binding(PublishedRecordKind.Action, A1, 1));

        Assert.Equal(("Succeeded:3", "Pending:0"), (await seeder.BriefQueueStatusAsync(Week), await seeder.BriefQueueStatusAsync(PriorWeek)));
    }

    [Theory]
    [InlineData(PublishedRecordKind.Statement)]
    [InlineData(PublishedRecordKind.DocumentChange)]
    [InlineData(PublishedRecordKind.Action)]
    public async Task APublishThatDropsARecordRequeuesTheBriefOfItsWeekForEachKind(PublishedRecordKind kind)
    {
        var id = kind switch { PublishedRecordKind.Statement => S2, PublishedRecordKind.DocumentChange => D1, _ => A1 };
        await seeder.BriefAsync(Week);
        var kept = new[]
        {
            Binding(PublishedRecordKind.Statement, S1, 1), Binding(PublishedRecordKind.Statement, S2, 1),
            Binding(PublishedRecordKind.DocumentChange, D1, 1), Binding(PublishedRecordKind.Action, A1, 1)
        }.Where(binding => binding.RecordId != id).ToArray();

        await CommitAsync(kept);

        Assert.Equal("Pending:0", await seeder.BriefQueueStatusAsync(Week));
    }

    [Fact]
    public async Task AWithdrawalRequeuesTheBriefOfTheWeekThatFirstPublishedTheWithdrawnRecord()
    {
        await seeder.BriefAsync(Week);
        await seeder.BriefAsync(PriorWeek);
        await seeder.ExecuteAsync($"""
            INSERT INTO action_drafts (record_id, source_id, congress, bill_type, bill_number, current_revision_number, created_at_utc_ticks, last_revised_at_utc_ticks, review_state_version, review_status)
            VALUES ('{A1}', 'congress-bills', 119, 'hr', 7, 1, 0, 0, 1, 'ChangesRequested')
            """);

        _ = await publications.CommitAsync("auth0|owner", new(3, 2, $"000003-{Guid.NewGuid():N}",
            [Binding(PublishedRecordKind.Statement, P1, 1), Binding(PublishedRecordKind.Statement, S1, 1),
             Binding(PublishedRecordKind.Statement, S2, 1), Binding(PublishedRecordKind.DocumentChange, D1, 1)], [])
        { WithdrawnRecords = [new(PublishedRecordKind.Action, A1)] }, "key-3",
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("payload"))), default);

        Assert.Equal(("Pending:0", "Succeeded:3"), (await seeder.BriefQueueStatusAsync(Week), await seeder.BriefQueueStatusAsync(PriorWeek)));
    }

    [Theory]
    [InlineData(BriefDraftErrorCodes.NoCitableRecords, "Pending:0")]
    [InlineData(BriefDraftErrorCodes.OverByteCap, "Blocked:3")]
    public async Task AChangedRecordRequeuesABlockedWeekOnlyWhenItHadNoRecordToCite(string errorCode, string expected)
    {
        await seeder.BriefAsync(Week);
        await seeder.ExecuteAsync($"UPDATE brief_analysis SET status = 'Blocked', error_code = '{errorCode}'");

        await CommitAsync(Binding(PublishedRecordKind.Statement, P1, 1), Binding(PublishedRecordKind.Statement, S1, 1),
            Binding(PublishedRecordKind.Statement, S2, 2), Binding(PublishedRecordKind.DocumentChange, D1, 1), Binding(PublishedRecordKind.Action, A1, 1));

        Assert.Equal(expected, await seeder.BriefQueueStatusAsync(Week));
    }

    [Fact]
    public async Task APublishThatOnlyAddsARecordLeavesTheFinishedBriefAloneBecauseTheNewRecordBelongsToTheOpenWeek()
    {
        await seeder.BriefAsync(Week);

        await CommitAsync(Binding(PublishedRecordKind.Statement, P1, 1), Binding(PublishedRecordKind.Statement, S1, 1),
            Binding(PublishedRecordKind.Statement, S2, 1), Binding(PublishedRecordKind.DocumentChange, D1, 1), Binding(PublishedRecordKind.Action, A1, 1),
            Binding(PublishedRecordKind.Statement, StatementId('9'), 1));

        Assert.Equal("Succeeded:3", await seeder.BriefQueueStatusAsync(Week));
    }

    [Fact]
    public async Task ARecordPublishedAgainAfterItWasDroppedRequeuesTheWeekThatFirstPublishedIt()
    {
        await seeder.BriefAsync(Week);
        await CommitAsync(3, 2, Binding(PublishedRecordKind.Statement, P1, 1), Binding(PublishedRecordKind.Statement, S2, 1),
            Binding(PublishedRecordKind.DocumentChange, D1, 1), Binding(PublishedRecordKind.Action, A1, 1));
        await seeder.ExecuteAsync("UPDATE brief_analysis SET status = 'Succeeded', attempts = 3");

        await CommitAsync(4, 3, Binding(PublishedRecordKind.Statement, P1, 1), Binding(PublishedRecordKind.Statement, S1, 1),
            Binding(PublishedRecordKind.Statement, S2, 1), Binding(PublishedRecordKind.DocumentChange, D1, 1), Binding(PublishedRecordKind.Action, A1, 1));

        Assert.Equal("Pending:0", await seeder.BriefQueueStatusAsync(Week));
    }

    [Fact]
    public async Task ARecordDroppedAndPublishedAgainInALaterWeekRequeuesOnlyTheWeekThatFirstPublishedIt()
    {
        var laterWeek = Week.AddDays(14);
        await seeder.ExecuteAsync("UPDATE publication_releases SET is_active = false WHERE release_number = 2");
        await seeder.ReleaseAsync(3, InWeek.AddDays(7), active: false, Binding(PublishedRecordKind.Statement, P1, 1),
            Binding(PublishedRecordKind.Statement, S1, 1), Binding(PublishedRecordKind.DocumentChange, D1, 1), Binding(PublishedRecordKind.Action, A1, 1));
        await seeder.ReleaseAsync(4, InWeek.AddDays(14), active: true, Binding(PublishedRecordKind.Statement, P1, 1),
            Binding(PublishedRecordKind.Statement, S1, 1), Binding(PublishedRecordKind.Statement, S2, 1),
            Binding(PublishedRecordKind.DocumentChange, D1, 1), Binding(PublishedRecordKind.Action, A1, 1));
        await seeder.BriefAsync(Week);
        await seeder.BriefAsync(laterWeek);

        _ = await CommitAsync(5, 4, Binding(PublishedRecordKind.Statement, P1, 1), Binding(PublishedRecordKind.Statement, S1, 1),
            Binding(PublishedRecordKind.Statement, S2, 2), Binding(PublishedRecordKind.DocumentChange, D1, 1), Binding(PublishedRecordKind.Action, A1, 1));

        Assert.Equal(("Pending:0", "Succeeded:3"), (await seeder.BriefQueueStatusAsync(Week), await seeder.BriefQueueStatusAsync(laterWeek)));
    }

    [Fact]
    public async Task ABriefThatIsApprovedIsNeverRequeuedByAPublishOrARollbackBecauseAReviewerHasAlreadyTakenResponsibilityForIt()
    {
        await seeder.BriefAsync(Week, reviewStatus: "Approved");

        await CommitAsync(Binding(PublishedRecordKind.Statement, P1, 1), Binding(PublishedRecordKind.DocumentChange, D1, 1),
            Binding(PublishedRecordKind.Action, A1, 1));
        Assert.Equal("Succeeded:3", await seeder.BriefQueueStatusAsync(Week));
        _ = await publications.ActivateAsync(2, (_, _) => Task.CompletedTask, default);

        Assert.Equal("Succeeded:3", await seeder.BriefQueueStatusAsync(Week));
    }

    [Fact]
    public async Task APendingBriefStaysAsItIsBecauseItReadsTheSelectionWhenItRunsAnyway()
    {
        await seeder.BriefAsync(Week, queueStatus: "Pending");

        await CommitAsync(Binding(PublishedRecordKind.Statement, P1, 1), Binding(PublishedRecordKind.DocumentChange, D1, 1),
            Binding(PublishedRecordKind.Action, A1, 1));

        Assert.Equal("Pending:3", await seeder.BriefQueueStatusAsync(Week));
    }

    [Fact]
    public async Task ARollbackRequeuesTheBriefOfTheAffectedWeekAndTheRedraftReadsTheOlderRevision()
    {
        await seeder.StatementAsync(S1, 1, "First", "warren");
        await seeder.StatementAsync(S2, 1, "Rent cap", "warren");
        await seeder.StatementAsync(S2, 2, "Rent cap amended", "warren");
        await seeder.DocumentChangeAsync(D1, 1, "A rule changed");
        await seeder.ActionAsync(A1, 1, 6, "warren");
        await seeder.DecisionAsync(PublishedRecordKind.Statement, S2, 2, 2, ReviewDecisionKind.Approve);
        _ = await briefs.RedraftAsync(Week, default);
        await CommitAsync(Binding(PublishedRecordKind.Statement, P1, 1), Binding(PublishedRecordKind.Statement, S1, 1),
            Binding(PublishedRecordKind.Statement, S2, 2), Binding(PublishedRecordKind.DocumentChange, D1, 1), Binding(PublishedRecordKind.Action, A1, 1));
        await seeder.ExecuteAsync("UPDATE brief_analysis SET status = 'Succeeded', attempts = 3");
        Assert.Equal(2, await ReadRevisionAsync(S2));

        _ = await publications.ActivateAsync(2, (_, _) => Task.CompletedTask, default);

        Assert.Equal("Pending:0", await seeder.BriefQueueStatusAsync(Week));
        Assert.Equal(1, await ReadRevisionAsync(S2));
    }

    [Fact]
    public async Task ACommitAfterARollbackLeavesEveryReleaseAnsweringItsOwnRevisionBecauseTheDiffIsAgainstTheHighestRelease()
    {
        await seeder.StatementAsync(S1, 1, "First", "warren");
        await seeder.StatementAsync(S2, 1, "Rent cap", "warren");
        await seeder.StatementAsync(S2, 2, "Rent cap amended", "warren");
        await seeder.DocumentChangeAsync(D1, 1, "A rule changed");
        await seeder.ActionAsync(A1, 1, 6, "warren");
        await seeder.DecisionAsync(PublishedRecordKind.Statement, S2, 2, 2, ReviewDecisionKind.Approve);
        _ = await briefs.RedraftAsync(Week, default);
        PublishedRevisionBinding[] Others() => [Binding(PublishedRecordKind.Statement, P1, 1), Binding(PublishedRecordKind.Statement, S1, 1),
            Binding(PublishedRecordKind.DocumentChange, D1, 1), Binding(PublishedRecordKind.Action, A1, 1)];
        await CommitAsync(3, 2, [.. Others(), Binding(PublishedRecordKind.Statement, S2, 2)]);
        _ = await publications.ActivateAsync(2, (_, _) => Task.CompletedTask, default);

        await CommitAsync(4, 2, [.. Others(), Binding(PublishedRecordKind.Statement, S2, 1)]);
        Assert.Equal(1, await ReadRevisionAsync(S2));
        _ = await publications.ActivateAsync(3, (_, _) => Task.CompletedTask, default);
        Assert.Equal(2, await ReadRevisionAsync(S2));
        _ = await publications.ActivateAsync(2, (_, _) => Task.CompletedTask, default);
        Assert.Equal(1, await ReadRevisionAsync(S2));
    }

    [Fact]
    public async Task ARollbackToAReleaseWithoutTheRecordsRequeuesTheBriefAndTheRedraftLeavesThemOut()
    {
        _ = await briefs.RedraftAsync(Week, default);
        await seeder.ExecuteAsync("UPDATE brief_analysis SET status = 'Succeeded', attempts = 3");

        _ = await publications.ActivateAsync(1, (_, _) => Task.CompletedTask, default);

        Assert.Equal("Pending:0", await seeder.BriefQueueStatusAsync(Week));
        var evidence = (await briefs.ReadEvidenceAsync(BriefWeek.RecordIdOf(Week), default))!;
        Assert.Empty(evidence.Records);
        Assert.Equal(4, evidence.LeftOut);
    }

    [Fact]
    public async Task ThePublishWaitsForTheBriefLockLastInTheOrderBecauseAnAppendOfABriefMustNotSlipInDuringIt()
    {
        await using var holder = await HeldAdvisoryLock.AcquireAsync(connectionString, "civic-lens-brief-review");

        var committing = CommitAsync(Binding(PublishedRecordKind.Statement, P1, 1));
        await HeldAdvisoryLock.WaitForWaiterAsync(connectionString, "civic-lens-brief-review");
        Assert.False(committing.IsCompleted);
        await holder.DisposeAsync();

        await committing;
    }

    [Fact]
    public async Task ThePublishDoesNotHoldTheBriefLockWhileItWaitsForAnEarlierLockSoTheBriefLockStaysLastInTheOrder()
    {
        await using var holder = await HeldAdvisoryLock.AcquireAsync(connectionString, "civic-lens-action-review");
        var committing = CommitAsync(Binding(PublishedRecordKind.Statement, P1, 1));
        await HeldAdvisoryLock.WaitForWaiterAsync(connectionString, "civic-lens-action-review");

        var acquiring = HeldAdvisoryLock.AcquireAsync(connectionString, "civic-lens-brief-review");
        Assert.Same(acquiring, await Task.WhenAny(acquiring, Task.Delay(TimeSpan.FromSeconds(10))));
        await (await acquiring).DisposeAsync();
        await holder.DisposeAsync();

        await committing;
    }

    [Fact]
    public async Task ThePublishDoesNotHoldTheBriefLockWhileItWaitsForTheRelationshipLockSoTheRelationshipLockComesBeforeIt()
    {
        await using var holder = await HeldAdvisoryLock.AcquireAsync(connectionString, "civic-lens-relationship-review");
        var committing = CommitAsync(Binding(PublishedRecordKind.Statement, P1, 1));
        await HeldAdvisoryLock.WaitForWaiterAsync(connectionString, "civic-lens-relationship-review");

        var acquiring = HeldAdvisoryLock.AcquireAsync(connectionString, "civic-lens-brief-review");
        Assert.Same(acquiring, await Task.WhenAny(acquiring, Task.Delay(TimeSpan.FromSeconds(10))));
        await (await acquiring).DisposeAsync();
        await holder.DisposeAsync();

        await committing;
    }

    [Fact]
    public async Task TheReleaseTimeIsStampedAfterTheBriefLockSoABriefReadThatWaitedForTheCommitFindsTheRelease()
    {
        await using var holder = await HeldAdvisoryLock.AcquireAsync(connectionString, "civic-lens-brief-review");
        var committing = CommitAsync(Binding(PublishedRecordKind.Statement, P1, 1));
        await HeldAdvisoryLock.WaitForWaiterAsync(connectionString, "civic-lens-brief-review");
        var lockHeldUntil = await seeder.DatabaseTicksAsync();
        await holder.DisposeAsync();

        var release = await committing;

        Assert.True(release.PublishedAtUtc.UtcTicks >= lockHeldUntil);
    }

    [Fact]
    public async Task ARunningBriefIsRequeuedAndItsLateSettlementIsRefusedSoItCannotSaveAStaleRevision()
    {
        await seeder.BriefAsync(Week, queueStatus: "Pending");
        var claim = (await briefs.TryClaimAsync(BriefWeek.RecordIdOf(Week), TimeSpan.FromMinutes(5), default))!;

        await CommitAsync(Binding(PublishedRecordKind.Statement, P1, 1), Binding(PublishedRecordKind.DocumentChange, D1, 1),
            Binding(PublishedRecordKind.Action, A1, 1));

        var settled = await briefs.CheckpointAsync(BriefDraftCheckpoint.From(claim, BriefFixtures.Revision(2) with { RecordId = BriefWeek.RecordIdOf(Week) }),
            null, default);
        Assert.False(settled);
        Assert.Equal("Pending:0", await seeder.BriefQueueStatusAsync(Week));
    }

    private Task<PublicationReleaseSummary> CommitAsync(params PublishedRevisionBinding[] records) => CommitAsync(3, 2, records);

    private Task<PublicationReleaseSummary> CommitAsync(int number, int baseNumber, params PublishedRevisionBinding[] records) =>
        publications.CommitAsync("auth0|owner", new(number, baseNumber, $"{number:D6}-{Guid.NewGuid():N}", [.. records], []), $"key-{number}",
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("payload"))), default);

    private async Task<int> ReadRevisionAsync(string statementId) =>
        (await briefs.ReadEvidenceAsync(BriefWeek.RecordIdOf(Week), default))!.Records.Single(record => record.RecordId == statementId).RevisionNumber;
}
