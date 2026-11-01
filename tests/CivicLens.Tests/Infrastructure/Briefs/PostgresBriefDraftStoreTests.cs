using CivicLens.Application.Briefs;
using CivicLens.Application.Publication;
using CivicLens.Core.Briefs;
using CivicLens.Core.Review;
using CivicLens.Infrastructure.Analysis;
using CivicLens.Infrastructure.Briefs;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Publication;
using CivicLens.Publication.Contracts;
using CivicLens.Tests.Application.Briefs;
using CivicLens.Tests.Fixtures;
using CivicLens.Tests.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using static CivicLens.Tests.Fixtures.BriefWorldSeeder;

namespace CivicLens.Tests.Infrastructure.Briefs;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class PostgresBriefDraftStoreTests(PostgresCollection postgres) : IAsyncLifetime
{
    private static readonly DateOnly Week = BriefFixtures.Week;
    private static readonly DateTimeOffset BeforeWeek = new(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset InWeek = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset AfterWeek = new(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);
    private static readonly string S1 = StatementId('1');
    private static readonly string S2 = StatementId('2');
    private static readonly string S3 = StatementId('3');
    private static readonly string A1 = ActionId('a');
    private static readonly string A2 = ActionId('b');
    private static readonly string D1 = DocumentChangeId('d');

    private string connectionString = null!;
    private PooledDbContextFactory<CollectionAttemptDbContext> factory = null!;
    private PostgresBriefDraftStore store = null!;
    private PostgresPublicationStore publications = null!;
    private BriefWorldSeeder seeder = null!;

    public async Task InitializeAsync()
    {
        connectionString = await postgres.CreateMigratedDatabaseAsync();
        factory = new PooledDbContextFactory<CollectionAttemptDbContext>(
            new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString).Options);
        store = new PostgresBriefDraftStore(factory);
        publications = new PostgresPublicationStore(factory);
        seeder = new BriefWorldSeeder(connectionString);
    }

    public Task DisposeAsync() => postgres.DropDatabaseAsync(connectionString);

    [Theory]
    [InlineData(2026, 10, 5, "2026-10-05T04:00:00Z", "2026-10-12T04:00:00Z", 168)]
    [InlineData(2026, 3, 2, "2026-03-02T05:00:00Z", "2026-03-09T04:00:00Z", 167)]
    [InlineData(2026, 10, 26, "2026-10-26T04:00:00Z", "2026-11-02T05:00:00Z", 169)]
    public async Task TheWeekRunsFromMondayMidnightToTheNextMondayMidnightInNewYorkWithTheClockChangeExact(int year, int month, int day,
        string start, string end, int hours)
    {
        var week = new DateOnly(year, month, day);
        await seeder.QueueWeekAsync(week);

        var window = (await store.ReadEvidenceAsync(BriefWeek.RecordIdOf(week), default))!.Window;

        Assert.Equal((DateTimeOffset.Parse(start).UtcTicks, DateTimeOffset.Parse(end).UtcTicks), (window.StartUtcTicks, window.EndUtcTicks));
        Assert.Equal(hours, TimeSpan.FromTicks(window.EndUtcTicks - window.StartUtcTicks).TotalHours);
    }

    [Fact]
    public async Task ARecordIsNewWhenTheCutoffReleaseHasItAndTheReleaseBeforeTheWeekDoesNot()
    {
        await SeedWeekAsync();

        var evidence = (await store.ReadEvidenceAsync(BriefWeek.RecordIdOf(Week), default))!;

        Assert.Equal([(BriefCitationKind.DocumentChange, D1, 1), (BriefCitationKind.Statement, S2, 1), (BriefCitationKind.Action, A2, 1)],
            evidence.Records.Select(record => (record.Kind, record.RecordId, record.RevisionNumber)));
        Assert.Equal((2, 2), (evidence.Window.FirstReleaseNumber, evidence.Window.CutoffReleaseNumber));
    }

    [Fact]
    public async Task ANewRevisionOfARecordThatWasPublishedBeforeTheWeekIsNotNewSoTheBriefNeverRepeatsIt()
    {
        await SeedWeekAsync();

        var evidence = (await store.ReadEvidenceAsync(BriefWeek.RecordIdOf(Week), default))!;

        Assert.DoesNotContain(evidence.Records, record => record.RecordId == A1);
        Assert.DoesNotContain(evidence.Records, record => record.RecordId == S1);
    }

    [Fact]
    public async Task ARecordBelongsOnlyToTheWeekOfItsFirstReleaseEvenWhenAnEarlierReleaseDroppedItAndALaterOneListsItAgain()
    {
        var laterWeek = Week.AddDays(14);
        await seeder.StatementAsync(S1, 1, "Kept statement", "warren");
        await seeder.StatementAsync(S2, 1, "Returned statement", "warren");
        await seeder.ReleaseAsync(1, InWeek, active: false, Binding(PublishedRecordKind.Statement, S1, 1), Binding(PublishedRecordKind.Statement, S2, 1));
        await seeder.ReleaseAsync(2, InWeek.AddDays(7), active: false, Binding(PublishedRecordKind.Statement, S1, 1));
        await seeder.ReleaseAsync(3, InWeek.AddDays(14), active: true, Binding(PublishedRecordKind.Statement, S1, 1), Binding(PublishedRecordKind.Statement, S2, 1));
        await seeder.QueueWeekAsync(Week);
        await seeder.QueueWeekAsync(laterWeek);

        var first = (await store.ReadEvidenceAsync(BriefWeek.RecordIdOf(Week), default))!;
        var later = (await store.ReadEvidenceAsync(BriefWeek.RecordIdOf(laterWeek), default))!;

        Assert.Equal([S1, S2], first.Records.Select(record => record.RecordId).Order(StringComparer.Ordinal));
        Assert.Empty(later.Records);
    }

    [Fact]
    public async Task AReleaseAfterTheWeekEndsNeverEntersTheWeek()
    {
        await SeedWeekAsync();

        var evidence = (await store.ReadEvidenceAsync(BriefWeek.RecordIdOf(Week), default))!;

        Assert.DoesNotContain(evidence.Records, record => record.RecordId == S3);
        Assert.Equal(2, evidence.Window.CutoffReleaseNumber);
    }

    [Fact]
    public async Task TheBriefReadsTheExactRevisionTheReleaseNamesAndTheOfficialsOfEachRecord()
    {
        await SeedWeekAsync();

        var records = (await store.ReadEvidenceAsync(BriefWeek.RecordIdOf(Week), default))!.Records;

        var statement = records.Single(record => record.RecordId == S2);
        Assert.Equal("Rent cap", statement.Headline);
        Assert.Equal(["warren"], statement.OfficialIds.ToArray());
        var action = records.Single(record => record.RecordId == A2);
        Assert.Equal(("119 hr 7", 2), (action.Bill, action.SponsorshipEntries));
        Assert.Equal(["markey", "warren"], action.OfficialIds.Order().ToArray());
    }

    [Fact]
    public async Task AWeekWithoutAnyReleaseBeforeItsEndHasNoRecordsAndAnEmptyWindow()
    {
        _ = await store.RedraftAsync(Week, default);
        await seeder.ReleaseAsync(1, AfterWeek, active: true, Binding(PublishedRecordKind.Statement, S1, 1));

        var evidence = (await store.ReadEvidenceAsync(BriefWeek.RecordIdOf(Week), default))!;

        Assert.Empty(evidence.Records);
        Assert.Equal(0, evidence.Window.CutoffReleaseNumber);
    }

    [Fact]
    public async Task AReleaseInTheWeekThatAddsNothingNewGivesNoRecords()
    {
        _ = await store.RedraftAsync(Week, default);
        await seeder.StatementAsync(S1, 1, "Old");
        await seeder.ReleaseAsync(1, BeforeWeek, active: false, Binding(PublishedRecordKind.Statement, S1, 1));
        await seeder.ReleaseAsync(2, InWeek, active: true, Binding(PublishedRecordKind.Statement, S1, 1));

        var evidence = (await store.ReadEvidenceAsync(BriefWeek.RecordIdOf(Week), default))!;

        Assert.Empty(evidence.Records);
    }

    [Fact]
    public async Task AReleaseThatNamesARevisionThatIsNotStoredFailsLoudInsteadOfDroppingTheRecord()
    {
        _ = await store.RedraftAsync(Week, default);
        await seeder.ReleaseAsync(1, InWeek, active: true, Binding(PublishedRecordKind.Statement, S1, 1));

        await Assert.ThrowsAsync<InvalidDataException>(() => store.ReadEvidenceAsync(BriefWeek.RecordIdOf(Week), default));
    }

    [Fact]
    public async Task AWeekWithoutAQueueRowHasNoEvidence()
    {
        Assert.Null(await store.ReadEvidenceAsync(BriefWeek.RecordIdOf(Week), default));
    }

    [Fact]
    public async Task TheFirstEnqueueStoresThisMondayAsTheFirstWeekAndQueuesNothingBecauseThisWeekIsStillOpen()
    {
        await store.EnqueueClosedWeeksAsync(default);

        Assert.Equal(CurrentMonday(), await FirstWeekAsync());
        Assert.Equal(0L, await ScalarAsync("SELECT count(*) FROM brief_analysis"));
    }

    [Fact]
    public async Task EnqueueAddsEveryClosedWeekFromTheFirstWeekAndNeverEarlierOnes()
    {
        var current = CurrentMonday();
        await seeder.ExecuteAsync("INSERT INTO brief_schedule (task, first_week_start) VALUES ('brief-draft', @week)", ("week", current.AddDays(-21)));

        await store.EnqueueClosedWeeksAsync(default);

        Assert.Equal([current.AddDays(-21), current.AddDays(-14), current.AddDays(-7)],
            await WeeksAsync());
    }

    [Fact]
    public async Task EnqueueIsIdempotentAndLeavesAFinishedWeekAlone()
    {
        var current = CurrentMonday();
        await seeder.ExecuteAsync("INSERT INTO brief_schedule (task, first_week_start) VALUES ('brief-draft', @week)", ("week", current.AddDays(-14)));
        await store.EnqueueClosedWeeksAsync(default);
        await seeder.ExecuteAsync("UPDATE brief_analysis SET status = 'Succeeded', error_code = 'noNewRecords' WHERE week_start = @week", ("week", current.AddDays(-14)));

        await store.EnqueueClosedWeeksAsync(default);

        Assert.Equal(2, (await WeeksAsync()).Count);
        Assert.Equal(("Succeeded", "noNewRecords"), await StatusAsync(current.AddDays(-14)));
        Assert.Equal(("Pending", null), await StatusAsync(current.AddDays(-7)));
    }

    [Fact]
    public async Task RedraftQueuesAWeekBeforeTheFirstBriefWeekWhenNoRowExists()
    {
        var requeued = await store.RedraftAsync(Week, default);

        Assert.Equal(1, requeued);
        Assert.Equal(("Pending", null), await StatusAsync(Week));
        Assert.Equal(0, await store.RedraftAsync(Week, default));
    }

    [Theory]
    [InlineData("Succeeded", "unchanged")]
    [InlineData("Failed", "outputRejected")]
    [InlineData("Blocked", "overByteCap")]
    public async Task RedraftPutsAFinishedWeekBackToPendingWithItsAttemptsCleared(string status, string code)
    {
        _ = await store.RedraftAsync(Week, default);
        await seeder.ExecuteAsync("UPDATE brief_analysis SET status = @status, error_code = @code, attempts = 2", ("status", status), ("code", code));

        Assert.Equal(1, await store.RedraftAsync(Week, default));

        Assert.Equal(("Pending", null), await StatusAsync(Week));
        Assert.Equal(0L, await ScalarAsync("SELECT attempts::bigint FROM brief_analysis"));
    }

    [Fact]
    public async Task RedraftLeavesARunningWeekAloneSoItNeverStealsALeaseFromAWorker()
    {
        _ = await store.RedraftAsync(Week, default);
        await seeder.ExecuteAsync("UPDATE brief_analysis SET status = 'Running', lease_token = 'lease', lease_expires_at = 1, fence = 1");

        Assert.Equal(0, await store.RedraftAsync(Week, default));
        Assert.Equal("Running", (await StatusAsync(Week)).Status);
    }

    [Fact]
    public async Task TheWorkerWritesTheBriefAndItsRevisionInOneTransaction()
    {
        await SeedWeekAsync();
        var model = new FakeBriefDraftModel().Draft(BriefFixtures.Output(paragraphs: [("Warren backed a cap.", ["R1"]), ("A rule changed.", ["R2", "R3"])]));

        await DraftAsync(model);

        Assert.Equal(("Succeeded", null), await StatusAsync(Week));
        Assert.Equal(("AwaitingReview", 1L), await TupleAsync("SELECT review_status, current_revision_number::bigint FROM brief_drafts"));
        Assert.Equal(BriefWeek.RecordIdOf(Week), (string)(await ScalarAsync("SELECT record_id FROM brief_revisions"))!);
        Assert.Equal(("Drafted", 1L), await TupleAsync("SELECT r.outcome, count(*) FROM brief_analysis_runs r JOIN brief_revisions v ON v.run_id = r.run_id GROUP BY r.outcome"));
        Assert.Single(model.Requests);
    }

    [Fact]
    public async Task ARedraftOfUnchangedRecordsSettlesWithoutAModelCallOrANewRevision()
    {
        await SeedWeekAsync();
        await DraftAsync(new FakeBriefDraftModel().Draft(BriefFixtures.Output(paragraphs: [("Text.", ["R1"])])));
        _ = await store.RedraftAsync(Week, default);
        var silent = new FakeBriefDraftModel();

        await DraftAsync(silent);

        Assert.Empty(silent.Requests);
        Assert.Equal(("Succeeded", "unchanged"), await StatusAsync(Week));
        Assert.Equal(1L, await ScalarAsync("SELECT count(*) FROM brief_revisions"));
        Assert.Equal(1L, await ScalarAsync("SELECT count(*) FROM brief_analysis_runs WHERE reason = 'unchanged' AND outcome = 'Bypassed'"));
    }

    [Fact]
    public async Task ADifferentFingerprintAppendsRevisionTwo()
    {
        await SeedWeekAsync();
        await DraftAsync(new FakeBriefDraftModel().Draft(BriefFixtures.Output(paragraphs: [("Text.", ["R1"])])));
        await seeder.ReleaseWithoutApprovalsAsync(9, InWeek.AddHours(1), active: false,
            Binding(PublishedRecordKind.Statement, S2, 1), Binding(PublishedRecordKind.Statement, S3, 1));
        _ = await store.RedraftAsync(Week, default);

        await DraftAsync(new FakeBriefDraftModel().Draft(BriefFixtures.Output(paragraphs: [("Newer text.", ["R1"])])));

        Assert.Equal(2L, await ScalarAsync("SELECT current_revision_number::bigint FROM brief_drafts"));
        Assert.Equal(2L, await ScalarAsync("SELECT count(*) FROM brief_revisions"));
    }

    [Fact]
    public async Task AnApprovalThatLandsWhileTheModelRunsLeavesTheApprovedRevisionAndSettlesTheWeekAsUnchanged()
    {
        await SeedWeekAsync();
        await DraftAsync(new FakeBriefDraftModel().Draft(BriefFixtures.Output(paragraphs: [("Text.", ["R1"])])));
        await seeder.ReleaseWithoutApprovalsAsync(9, InWeek.AddHours(1), active: false,
            Binding(PublishedRecordKind.Statement, S2, 1), Binding(PublishedRecordKind.Statement, S3, 1));
        _ = await store.RedraftAsync(Week, default);

        var fingerprint = (await store.ReadEvidenceAsync(BriefWeek.RecordIdOf(Week), default))!.Current!.Fingerprint;

        await DraftAsync(new ApprovingModel(seeder, BriefFixtures.Output(paragraphs: [("Newer text.", ["R1"])])));

        Assert.Equal(("Succeeded", BriefDraftErrorCodes.Unchanged), await StatusAsync(Week));
        Assert.Equal(1L, await ScalarAsync("SELECT count(*) FROM brief_revisions"));
        Assert.Equal("Approved", await ScalarAsync("SELECT review_status FROM brief_drafts"));
        Assert.Equal(1L, await ScalarAsync("SELECT count(*) FROM brief_analysis_runs WHERE outcome = 'DraftDiscarded' AND reason IS NULL"));
        Assert.Equal(fingerprint, (await store.ReadEvidenceAsync(BriefWeek.RecordIdOf(Week), default))!.Current!.Fingerprint);
    }

    [Fact]
    public async Task ARedraftOfAWeekWithAnApprovedBriefIsRefusedWithoutAModelCallUntilTheApprovalIsWithdrawn()
    {
        await SeedWeekAsync();
        await DraftAsync(new FakeBriefDraftModel().Draft(BriefFixtures.Output(paragraphs: [("Text.", ["R1"])])));
        await seeder.ExecuteAsync("UPDATE brief_drafts SET review_status = 'Approved'");

        var refused = await Assert.ThrowsAsync<ArgumentException>(() => store.RedraftAsync(Week, default));

        Assert.Contains("Withdraw the approval", refused.Message, StringComparison.Ordinal);
        Assert.Equal(("Succeeded", null), await StatusAsync(Week));
        await seeder.ExecuteAsync("UPDATE brief_drafts SET review_status = 'ApprovalWithdrawn'");
        Assert.Equal(1, await store.RedraftAsync(Week, default));
    }

    [Fact]
    public async Task AWeekWithNoNewRecordsSettlesWithNoBriefAndABypassedRun()
    {
        _ = await store.RedraftAsync(Week, default);
        var silent = new FakeBriefDraftModel();

        await DraftAsync(silent);

        Assert.Empty(silent.Requests);
        Assert.Equal(("Succeeded", "noNewRecords"), await StatusAsync(Week));
        Assert.Equal(0L, await ScalarAsync("SELECT count(*) FROM brief_drafts"));
        Assert.Equal(("Bypassed", "noNewRecords"), await PairAsync("SELECT outcome, reason FROM brief_analysis_runs"));
    }

    [Fact]
    public async Task ARevisionThatFindsADifferentCurrentRevisionIsDiscardedAndRetriedSoTwoDraftersNeverShareARevisionNumber()
    {
        await SeedWeekAsync();
        var model = new ConcurrentWriterModel(seeder, BriefFixtures.Output(paragraphs: [("Text.", ["R1"])]));

        await DraftAsync(model);

        Assert.Equal(("RetryWaiting", BriefDraftErrorCodes.RevisionConflict), await StatusAsync(Week));
        Assert.Equal(("DraftDiscarded", 1L), await TupleAsync("SELECT outcome, count(*) FROM brief_analysis_runs GROUP BY outcome"));
        Assert.Equal(1L, await ScalarAsync("SELECT count(*) FROM brief_revisions"));
    }

    [Fact]
    public async Task TheBriefTableRefusesAWeekThatDoesNotStartOnAMondayOrAStatusThatThePolicyNeverStores()
    {
        await Assert.ThrowsAsync<PostgresException>(() => seeder.ExecuteAsync(
            $"INSERT INTO brief_drafts (record_id, week_start, current_revision_number, review_state_version, review_status, created_at_utc_ticks, last_revised_at_utc_ticks) VALUES ('{new string('a', 64)}', '2026-10-06', 1, 0, 'AwaitingReview', 1, 1)"));
        await Assert.ThrowsAsync<PostgresException>(() => seeder.ExecuteAsync(
            $"INSERT INTO brief_drafts (record_id, week_start, current_revision_number, review_state_version, review_status, created_at_utc_ticks, last_revised_at_utc_ticks) VALUES ('{new string('a', 64)}', '2026-10-05', 1, 0, 'Published', 1, 1)"));
    }

    [Fact]
    public async Task OneWeekHasOneBriefAndOneQueueRowBecauseTheWeekIsUnique()
    {
        _ = await store.RedraftAsync(Week, default);

        await Assert.ThrowsAsync<PostgresException>(() => seeder.ExecuteAsync(
            $"INSERT INTO brief_analysis (record_id, task, week_start, status, fence, attempts, reserved_tokens, created_at_utc_ticks) VALUES ('{new string('b', 64)}', 'brief-draft', '2026-10-05', 'Pending', 0, 0, 0, 1)"));
    }

    [Fact]
    public async Task TheBriefWakeupQueryRunsAndWaitsForWorkInsteadOfFailing()
    {
        await using var wakeup = PostgresBriefDraftStore.Wakeup(connectionString);
        await wakeup.ConnectAsync(default);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => wakeup.WaitAsync(timeout.Token));
    }

    [Fact]
    public async Task ARequeueReadsEachRecordAtItsRevisionInTheActiveReleaseAndKeepsTheWeekSetFixed()
    {
        await SeedWeekAsync();
        await seeder.StatementAsync(S2, 2, "Rent cap amended", "warren");
        await seeder.DecisionAsync(PublishedRecordKind.Statement, S2, 2, 2, ReviewDecisionKind.Approve);
        await PublishAsync(4, Binding(PublishedRecordKind.Statement, S2, 2), Binding(PublishedRecordKind.Action, A2, 1),
            Binding(PublishedRecordKind.DocumentChange, D1, 1), Binding(PublishedRecordKind.Statement, S1, 1), Binding(PublishedRecordKind.Action, A1, 2));

        var evidence = (await store.ReadEvidenceAsync(BriefWeek.RecordIdOf(Week), default))!;

        Assert.Equal([(BriefCitationKind.DocumentChange, D1, 1), (BriefCitationKind.Statement, S2, 2), (BriefCitationKind.Action, A2, 1)],
            evidence.Records.Select(record => (record.Kind, record.RecordId, record.RevisionNumber)));
        Assert.Equal("Rent cap amended", evidence.Records.Single(record => record.RecordId == S2).Headline);
        Assert.Equal((2, 0), (evidence.Window.CutoffReleaseNumber, evidence.LeftOut));
    }

    [Fact]
    public async Task ARecordTheActiveReleaseNoLongerHoldsIsLeftOutAndCounted()
    {
        await SeedWeekAsync();
        await PublishAsync(4, Binding(PublishedRecordKind.Statement, S2, 1), Binding(PublishedRecordKind.Action, A2, 1),
            Binding(PublishedRecordKind.Statement, S1, 1), Binding(PublishedRecordKind.Action, A1, 2));

        var evidence = (await store.ReadEvidenceAsync(BriefWeek.RecordIdOf(Week), default))!;

        Assert.DoesNotContain(evidence.Records, record => record.RecordId == D1);
        Assert.Equal(1, evidence.LeftOut);
    }

    [Theory]
    [InlineData(ReviewDecisionKind.WithdrawApproval, true)]
    [InlineData(ReviewDecisionKind.RequestChanges, true)]
    [InlineData(ReviewDecisionKind.Approve, false)]
    public async Task ARecordWhoseLatestDecisionOnThePublishedRevisionIsNotAnApprovalIsLeftOutAndCounted(ReviewDecisionKind latest, bool leftOut)
    {
        await SeedWeekAsync();
        await seeder.DecisionAsync(PublishedRecordKind.Statement, S2, 1, 10, latest);

        var evidence = (await store.ReadEvidenceAsync(BriefWeek.RecordIdOf(Week), default))!;

        Assert.Equal(leftOut, evidence.Records.All(record => record.RecordId != S2));
        Assert.Equal(leftOut ? 1 : 0, evidence.LeftOut);
    }

    [Fact]
    public async Task ARecordApprovedAgainAfterAWithdrawalOfThePublishedRevisionIsInTheSelectionAgain()
    {
        await SeedWeekAsync();
        await seeder.DecisionAsync(PublishedRecordKind.Statement, S2, 1, 10, ReviewDecisionKind.WithdrawApproval);
        await seeder.DecisionAsync(PublishedRecordKind.Statement, S2, 1, 11, ReviewDecisionKind.Approve);

        var evidence = (await store.ReadEvidenceAsync(BriefWeek.RecordIdOf(Week), default))!;

        Assert.Contains(evidence.Records, record => record.RecordId == S2);
        Assert.Equal(0, evidence.LeftOut);
    }

    [Fact]
    public async Task ADraftOnANewerRevisionDoesNotWithdrawThePublishedRevisionBecauseTheActiveReleaseStillServesIt()
    {
        await SeedWeekAsync();
        await seeder.StatementAsync(S2, 2, "Rent cap amended", "warren");

        var evidence = (await store.ReadEvidenceAsync(BriefWeek.RecordIdOf(Week), default))!;

        Assert.Contains(evidence.Records, record => record.RecordId == S2 && record.RevisionNumber == 1);
        Assert.Equal(0, evidence.LeftOut);
    }

    [Fact]
    public async Task ARequeueAfterANewRevisionIsPublishedAppendsARevisionThatReadsTheNewRevision()
    {
        await SeedWeekAsync();
        await seeder.StatementAsync(S2, 2, "Rent cap amended", "warren");
        await seeder.DecisionAsync(PublishedRecordKind.Statement, S2, 2, 2, ReviewDecisionKind.Approve);
        await DraftAsync(new FakeBriefDraftModel().Draft(CitesEveryRecord));
        await PublishAsync(4, Binding(PublishedRecordKind.Statement, S2, 2), Binding(PublishedRecordKind.Action, A2, 1),
            Binding(PublishedRecordKind.DocumentChange, D1, 1), Binding(PublishedRecordKind.Statement, S1, 1), Binding(PublishedRecordKind.Action, A1, 2));
        Assert.Equal(("Pending", null), await StatusAsync(Week));

        await DraftAsync(new FakeBriefDraftModel().Draft(CitesEveryRecord));

        Assert.Equal(2L, await ScalarAsync("SELECT count(*) FROM brief_revisions"));
        Assert.Equal((1, 2), (await CitedRevisionAsync(1, S2), await CitedRevisionAsync(2, S2)));
    }

    [Fact]
    public async Task ARequeueAfterAnApprovalIsWithdrawnAppendsARevisionThatLeavesTheRecordOut()
    {
        await SeedWeekAsync();
        await DraftAsync(new FakeBriefDraftModel().Draft(CitesEveryRecord));
        await seeder.DecisionAsync(PublishedRecordKind.Statement, S2, 1, 10, ReviewDecisionKind.WithdrawApproval);
        _ = await store.RedraftAsync(Week, default);

        await DraftAsync(new FakeBriefDraftModel().Draft(BriefFixtures.Output(paragraphs: [("Text.", ["R1", "R2"])])));

        Assert.Equal(2L, await ScalarAsync("SELECT count(*) FROM brief_revisions"));
        Assert.Null(await CitedRevisionAsync(2, S2));
        Assert.Contains("\"considered\":3", await ScalarTextAsync("SELECT revision_json FROM brief_revisions WHERE revision_number = 2"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AStaleSignalWhileTheBriefIsRunningLeadsToANewDraftAndNeverASavedStaleRevision()
    {
        await SeedWeekAsync();
        await seeder.StatementAsync(S2, 2, "Rent cap amended", "warren");
        await seeder.StatementAsync(S2, 3, "Rent cap amended again", "warren");
        await seeder.DecisionAsync(PublishedRecordKind.Statement, S2, 3, 3, ReviewDecisionKind.Approve);
        await DraftAsync(new FakeBriefDraftModel().Draft(CitesEveryRecord));
        await PublishAsync(4, Binding(PublishedRecordKind.Statement, S2, 2), Binding(PublishedRecordKind.Action, A2, 1),
            Binding(PublishedRecordKind.DocumentChange, D1, 1), Binding(PublishedRecordKind.Statement, S1, 1), Binding(PublishedRecordKind.Action, A1, 2));
        var publishingDuringTheCall = new PublishingModel(() => PublishAsync(5, Binding(PublishedRecordKind.Statement, S2, 3),
            Binding(PublishedRecordKind.Action, A2, 1), Binding(PublishedRecordKind.DocumentChange, D1, 1), Binding(PublishedRecordKind.Statement, S1, 1),
            Binding(PublishedRecordKind.Action, A1, 2)), CitesEveryRecord);

        await DraftAsync(publishingDuringTheCall);

        Assert.Equal(1L, await ScalarAsync("SELECT count(*) FROM brief_revisions"));
        Assert.Equal(("Pending", null), await StatusAsync(Week));
        Assert.Equal(1L, await ScalarAsync("SELECT count(*) FROM brief_analysis_runs WHERE outcome = 'Interrupted'"));

        await DraftAsync(new FakeBriefDraftModel().Draft(CitesEveryRecord));

        Assert.Equal(2L, await ScalarAsync("SELECT count(*) FROM brief_revisions"));
        Assert.Equal(3, await CitedRevisionAsync(2, S2));
    }

    [Fact]
    public async Task TheSelectionReadWaitsForACommitInFlightBecauseItTakesTheBriefLock()
    {
        _ = await store.RedraftAsync(Week, default);
        await using var holder = await HeldAdvisoryLock.AcquireAsync(connectionString, "civic-lens-brief-review");

        var reading = store.ReadEvidenceAsync(BriefWeek.RecordIdOf(Week), default);
        await HeldAdvisoryLock.WaitForWaiterAsync(connectionString, "civic-lens-brief-review");
        Assert.False(reading.IsCompleted);
        await holder.DisposeAsync();

        Assert.NotNull(await reading);
    }

    [Fact]
    public async Task RedraftRefusesAWeekThatHasNotEndedBecauseItsRecordsCanStillChange()
    {
        var current = CurrentMonday();

        await Assert.ThrowsAsync<ArgumentException>(() => store.RedraftAsync(current, default));
        await Assert.ThrowsAsync<ArgumentException>(() => store.RedraftAsync(current.AddDays(7), default));
        Assert.Equal(0L, await ScalarAsync("SELECT count(*) FROM brief_analysis"));
        Assert.Equal(1, await store.RedraftAsync(current.AddDays(-7), default));
    }

    [Theory]
    [InlineData("2026-10-18 23:59:58-04")]
    [InlineData("2026-11-01 23:59:58-05")]
    public async Task TheBriefWakeupFiresAtTheNextMondayMidnightInNewYorkEvenWithNoQueuedWork(string databaseNow)
    {
        var database = new NpgsqlConnectionStringBuilder(connectionString);
        await seeder.ExecuteAsync($"CREATE FUNCTION public.clock_timestamp() RETURNS timestamptz LANGUAGE sql STABLE AS $$ SELECT timestamptz '{databaseNow}' $$");
        await seeder.ExecuteAsync($"ALTER DATABASE \"{database.Database}\" SET search_path = public, pg_catalog");
        database.Pooling = false;
        await using var wakeup = PostgresBriefDraftStore.Wakeup(database.ConnectionString);
        await wakeup.ConnectAsync(default);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var stopwatch = Stopwatch.StartNew();

        await wakeup.WaitAsync(timeout.Token);

        Assert.InRange(stopwatch.Elapsed, TimeSpan.FromSeconds(1.5), TimeSpan.FromSeconds(6));
    }

    private async Task SeedWeekAsync()
    {
        _ = await store.RedraftAsync(Week, default);
        await seeder.StatementAsync(S1, 1, "Old statement", "warren");
        await seeder.StatementAsync(S2, 1, "Rent cap", "warren");
        await seeder.StatementAsync(S3, 1, "Later statement", "markey");
        await seeder.ActionAsync(A1, 1, 6, "warren");
        await seeder.ActionAsync(A1, 2, 6, "warren", "markey");
        await seeder.ActionAsync(A2, 1, 7, "warren", "markey");
        await seeder.DocumentChangeAsync(D1, 1, "A rule changed");
        await seeder.ReleaseAsync(1, BeforeWeek, active: false,
            Binding(PublishedRecordKind.Statement, S1, 1), Binding(PublishedRecordKind.Action, A1, 1));
        await seeder.ReleaseAsync(2, InWeek, active: false,
            Binding(PublishedRecordKind.Statement, S1, 1), Binding(PublishedRecordKind.Action, A1, 2),
            Binding(PublishedRecordKind.Statement, S2, 1), Binding(PublishedRecordKind.Action, A2, 1), Binding(PublishedRecordKind.DocumentChange, D1, 1));
        await seeder.ReleaseAsync(3, AfterWeek, active: true,
            Binding(PublishedRecordKind.Statement, S1, 1), Binding(PublishedRecordKind.Action, A1, 2),
            Binding(PublishedRecordKind.Statement, S2, 1), Binding(PublishedRecordKind.Action, A2, 1), Binding(PublishedRecordKind.DocumentChange, D1, 1),
            Binding(PublishedRecordKind.Statement, S3, 1));
    }

    private static readonly string CitesEveryRecord = BriefFixtures.Output(paragraphs: [("Warren backed a cap.", ["R1", "R2", "R3"])]);

    private Task PublishAsync(int number, params PublishedRevisionBinding[] records) => publications.CommitAsync("auth0|owner",
        new(number, number - 1, $"{number:D6}-{Guid.NewGuid():N}", [.. records], []), $"key-{number}",
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"payload-{number}"))), default);

    private async Task<int?> CitedRevisionAsync(int briefRevision, string recordId)
    {
        using var revision = JsonDocument.Parse(await ScalarTextAsync($"SELECT revision_json FROM brief_revisions WHERE revision_number = {briefRevision}"));
        return revision.RootElement.GetProperty("citations").EnumerateArray()
            .Where(citation => citation.GetProperty("recordId").GetString() == recordId)
            .Select(citation => (int?)citation.GetProperty("revisionNumber").GetInt32()).SingleOrDefault();
    }

    private async Task<string> ScalarTextAsync(string sql) => (string)(await ScalarAsync(sql))!;

    private Task DraftAsync(IBriefDraftModel model) => AnalysisWorkers.BriefDrafting(store,
        new PostgresAnalysisStatusStore(factory, BriefDraftTask.Task), model, new BriefDraftSettings(10_000_000, concurrency: 1),
        BriefFixtures.Officials, TimeProvider.System).ExecuteAsync(once: true);

    private static DateOnly CurrentMonday()
    {
        var now = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("America/New_York"));
        var date = DateOnly.FromDateTime(now.DateTime);
        return date.AddDays(-(((int)date.DayOfWeek + 6) % 7));
    }

    private async Task<DateOnly> FirstWeekAsync() => (DateOnly)(await ScalarAsync("SELECT first_week_start FROM brief_schedule"))!;

    private async Task<List<DateOnly>> WeeksAsync()
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT week_start FROM brief_analysis ORDER BY week_start", connection);
        await using var reader = await command.ExecuteReaderAsync();
        var weeks = new List<DateOnly>();
        while (await reader.ReadAsync()) weeks.Add(reader.GetFieldValue<DateOnly>(0));
        return weeks;
    }

    private async Task<(string Status, string? Code)> StatusAsync(DateOnly week)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT status, error_code FROM brief_analysis WHERE week_start = @week", connection);
        command.Parameters.AddWithValue("week", week);
        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();
        return (reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1));
    }

    private async Task<object?> ScalarAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return await command.ExecuteScalarAsync();
    }

    private async Task<(string, long)> TupleAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();
        return (reader.GetString(0), reader.GetInt64(1));
    }

    private async Task<(string, string)> PairAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();
        return (reader.GetString(0), reader.GetString(1));
    }

    private sealed class PublishingModel(Func<Task> publish, string reply) : IBriefDraftModel
    {
        public async Task<CivicLens.Application.Analysis.AnalysisModelResponse> DraftAsync(CivicLens.Application.Analysis.AnalysisModelRequest request,
            CancellationToken cancellationToken)
        {
            await publish();
            return new("end_turn", reply, new(900, 100, 0, 0), "req_fixture");
        }
    }

    private sealed class ConcurrentWriterModel(BriefWorldSeeder seeder, string reply) : IBriefDraftModel
    {
        public async Task<CivicLens.Application.Analysis.AnalysisModelResponse> DraftAsync(CivicLens.Application.Analysis.AnalysisModelRequest request,
            CancellationToken cancellationToken)
        {
            await seeder.ExecuteAsync($"""
                SET session_replication_role = replica;
                INSERT INTO brief_drafts (record_id, week_start, current_revision_number, review_state_version, review_status, created_at_utc_ticks, last_revised_at_utc_ticks)
                VALUES ('{BriefWeek.RecordIdOf(BriefFixtures.Week)}', '2026-10-05', 1, 0, 'AwaitingReview', 1, 1);
                INSERT INTO brief_revisions (record_id, revision_number, author_subject, author_kind, run_id, revision_json, created_at_utc_ticks)
                VALUES ('{BriefWeek.RecordIdOf(BriefFixtures.Week)}', 1, 'auth0|editor', 'Person', NULL, 'x', 1);
                """);
            return new("end_turn", reply, new(900, 100, 0, 0), "req_fixture");
        }
    }

    private sealed class ApprovingModel(BriefWorldSeeder seeder, string reply) : IBriefDraftModel
    {
        public async Task<CivicLens.Application.Analysis.AnalysisModelResponse> DraftAsync(CivicLens.Application.Analysis.AnalysisModelRequest request,
            CancellationToken cancellationToken)
        {
            await seeder.ExecuteAsync("UPDATE brief_drafts SET review_status = 'Approved'");
            return new("end_turn", reply, new(900, 100, 0, 0), "req_fixture");
        }
    }
}
