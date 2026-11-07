using System.Data.Common;
using CivicLens.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using System.Collections.Immutable;
using CivicLens.Application.Collection.Bills;
using CivicLens.Application.Publication;
using CivicLens.Infrastructure.Publication;
using CivicLens.Tests.Fixtures;
using Npgsql;

namespace CivicLens.Tests.Infrastructure.Collection.BillStream;

public sealed partial class PostgresBillStreamTests
{
    [Fact]
    public async Task ANewStatusCanBePublishedWithThePreviousCompletedHistoryWhileTheNewHistoryIsIncomplete()
    {
        await DraftForReviewAsync(UpdatedAt, DraftableOutput);
        var previousDetail = (string)(await ScalarAsync("SELECT detail_job_id FROM bill_gates"))!;
        var newer = await EnqueueDetailAsync("newer-history");
        await AdmitDetailAsync(newer,
            DetailBill(UpdatedAt.AddDays(1), cosponsors: 0, latestAction: new(new DateOnly(2025, 3, 4), "Passed Senate.")),
            BillGateOutcome.Passed(BillGateReason.CoveredSponsor));

        var input = Assert.Single(await ReadBillInputsAsync([RecordId]));

        Assert.Equal("Passed Senate.", input.Status!.Status.Text);
        Assert.Equal(new DateOnly(2025, 3, 4), input.Status.Status.Date);
        Assert.Equal(previousDetail, input.History!.DetailJobId);
        Assert.Equal(0, input.History.ActionCount);
    }

    [Fact]
    public async Task AClosedEmptyActionsChainIsAHistoryAndAnUnclosedNewerChainIsNot()
    {
        await DraftForReviewAsync(UpdatedAt, DraftableOutput);
        var previousDetail = (string)(await ScalarAsync("SELECT detail_job_id FROM bill_gates"))!;
        var initial = Assert.Single(await ReadBillInputsAsync([RecordId]));
        Assert.Equal(previousDetail, initial.History!.DetailJobId);
        Assert.Equal(0, initial.History.ActionCount);

        var newer = await EnqueueDetailAsync("unclosed-history");
        var result = await AdmitDetailAsync(newer,
            DetailBill(UpdatedAt.AddDays(1), cosponsors: 0), BillGateOutcome.Passed(BillGateReason.CoveredSponsor));
        var newerActions = result.Jobs.Single(job => job.Url.Contains("/actions?")).JobId;
        var next = await AdmitActionsAsync(newerActions,
            new CongressBillActionsPage([HistoryAction("Not yet complete.")], ActionsNext(250)));
        await AdmitActionsAsync(next.Jobs.Single().JobId, new CongressBillActionsPage([HistoryAction("Still open.")], ActionsNext(500)));

        var input = Assert.Single(await ReadBillInputsAsync([RecordId]));

        Assert.Equal(previousDetail, input.History!.DetailJobId);
        Assert.Equal(0, input.History.ActionCount);
    }

    [Fact]
    public async Task ANewCompletedHistoryIsSelectedEvenWhenTheLatestStatusTextDoesNotChange()
    {
        await DraftForReviewAsync(UpdatedAt, DraftableOutput);
        var status = new CongressBillLatestAction(new DateOnly(2025, 3, 4), "Passed Senate.");
        var incompleteDetail = await EnqueueDetailAsync("same-status-incomplete");
        await AdmitDetailAsync(incompleteDetail,
            DetailBill(UpdatedAt.AddDays(1), cosponsors: 0, latestAction: status), BillGateOutcome.Passed(BillGateReason.CoveredSponsor));
        var before = Assert.Single(await ReadBillInputsAsync([RecordId]));
        var completedDetail = await EnqueueDetailAsync("same-status-complete");
        var detail = await AdmitDetailAsync(completedDetail,
            DetailBill(UpdatedAt.AddDays(2), cosponsors: 0, latestAction: status), BillGateOutcome.Passed(BillGateReason.CoveredSponsor));
        var actions = detail.Jobs.Single(job => job.Url.Contains("/actions?")).JobId;
        await AdmitActionsAsync(actions, new CongressBillActionsPage([HistoryAction("New completed action.")], null));

        var after = Assert.Single(await ReadBillInputsAsync([RecordId]));

        Assert.Equal(before.Status!.Status.Text, after.Status!.Status.Text);
        Assert.Equal(before.Status.Status.Date, after.Status.Status.Date);
        Assert.Equal(completedDetail, after.History!.DetailJobId);
        Assert.NotEqual(before.History!.DetailJobId, after.History.DetailJobId);
    }

    [Fact]
    public async Task TheHistoryObservationTimeIsTheLatestCapturedPageTimeNotTheGateTime()
    {
        await DraftForReviewAsync(UpdatedAt, DraftableOutput);
        var newer = await EnqueueDetailAsync("history-pages");
        var detail = await AdmitDetailAsync(newer, DetailBill(UpdatedAt.AddDays(1), cosponsors: 0),
            BillGateOutcome.Passed(BillGateReason.CoveredSponsor));
        var actions = detail.Jobs.Single(job => job.Url.Contains("/actions?")).JobId;
        var first = await AdmitActionsAsync(actions,
            new CongressBillActionsPage([HistoryAction("First page.")], ActionsNext(250)));
        await AdmitActionsAsync(first.Jobs.Single().JobId, new CongressBillActionsPage([HistoryAction("Last page.")], null));

        var input = Assert.Single(await ReadBillInputsAsync([RecordId]));
        var maxPageObservation = (long)(await ScalarWithParametersAsync("""
            SELECT max(c.observed_at_utc_ticks)
              FROM bill_pages p
              JOIN document_extractions e ON e.extraction_id = p.extraction_id
              JOIN collection_attempts c ON c.attempt_id = e.attempt_id
             WHERE p.detail_job_id = @detail AND p.role = 'Actions'
            """, new NpgsqlParameter("detail", newer)))!;
        var gateCreatedAt = (long)(await ScalarWithParametersAsync("SELECT created_at_utc_ticks FROM bill_gates WHERE detail_job_id = @detail",
            new NpgsqlParameter("detail", newer)))!;

        Assert.Equal(maxPageObservation, input.History!.ObservedAtUtc.UtcTicks);
        Assert.NotEqual(gateCreatedAt, input.History.ObservedAtUtc.UtcTicks);
        Assert.Equal(2, input.History.ActionCount);
    }

    [Fact]
    public async Task PreparingHistoryCopiesTypedActionsOnceAndReusesTheImmutableCopy()
    {
        await DraftForReviewAsync(UpdatedAt, DraftableOutput);
        var newer = await EnqueueDetailAsync("typed-history");
        var detail = await AdmitDetailAsync(newer, DetailBill(UpdatedAt.AddDays(1), cosponsors: 0),
            BillGateOutcome.Passed(BillGateReason.CoveredSponsor));
        var actions = detail.Jobs.Single(job => job.Url.Contains("/actions?")).JobId;
        var action = new CongressBillAction(new DateOnly(2025, 3, 2), new TimeOnly(13, 45, 10), "Passed House.",
            "Floor", "H8D000", 2, "House floor actions", []);
        await AdmitActionsAsync(actions, new CongressBillActionsPage([action, HistoryAction("Introduced.")], null));
        var capture = Assert.Single(await ReadBillInputsAsync([RecordId])).History!;
        var publications = PostgresPublicationStore.FromConnectionString(connectionString, TestCodeVersion.Current);

        await publications.PrepareBillHistoriesAsync([capture], default);
        await ExecuteAsync(connectionString, "UPDATE bill_actions SET text = 'Changed later.', action_time = NULL, type = NULL WHERE position = 0");
        await publications.PrepareBillHistoriesAsync([capture], default);

        Assert.Equal(1L, await ScalarWithParametersAsync("SELECT count(*) FROM published_bill_histories WHERE detail_job_id = @detail",
            new NpgsqlParameter("detail", capture.DetailJobId)));
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            SELECT position, action_date::text, action_time::text, text, type, action_code, source_system_code, source_system_name
              FROM published_bill_history_actions WHERE detail_job_id = @detail ORDER BY position
            """, connection);
        command.Parameters.AddWithValue("detail", capture.DetailJobId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal((0, "2025-03-02", "13:45:10", "Passed House.", "Floor", "H8D000", 2, "House floor actions"),
            (reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4),
                reader.GetString(5), reader.GetInt32(6), reader.GetString(7)));
        Assert.True(await reader.ReadAsync());
        Assert.Equal(1, reader.GetInt32(0));
        Assert.Equal("2025-03-01", reader.GetString(1));
        Assert.Equal("Introduced.", reader.GetString(3));
        foreach (var ordinal in new[] { 2, 4, 5, 6, 7 }) Assert.True(reader.IsDBNull(ordinal));
        Assert.False(await reader.ReadAsync());
    }

    [Fact]
    public async Task AHistoryCaptureWithWrongMetadataIsRejectedBeforeAnyCopyIsWritten()
    {
        await DraftForReviewAsync(UpdatedAt, DraftableOutput);
        var newer = await EnqueueDetailAsync("malformed-history-capture");
        var detail = await AdmitDetailAsync(newer, DetailBill(UpdatedAt.AddDays(1), cosponsors: 0),
            BillGateOutcome.Passed(BillGateReason.CoveredSponsor));
        var actions = detail.Jobs.Single(job => job.Url.Contains("/actions?")).JobId;
        await AdmitActionsAsync(actions, new CongressBillActionsPage([HistoryAction("Action.")], null));
        var capture = Assert.Single(await ReadBillInputsAsync([RecordId])).History!;
        var publications = PostgresPublicationStore.FromConnectionString(connectionString, TestCodeVersion.Current);

        await Assert.ThrowsAsync<PublicationConflictException>(() => publications.PrepareBillHistoriesAsync(
            [capture with { BillNumber = capture.BillNumber + 1 }], default));

        Assert.Equal(0L, await ScalarAsync("SELECT count(*) FROM published_bill_histories"));
        Assert.Equal(0L, await ScalarAsync("SELECT count(*) FROM published_bill_history_actions"));
    }

    [Fact]
    public async Task PublicLawComesOnlyFromAnExactOfficialActionInTheCompletedCopy()
    {
        await DraftForReviewAsync(UpdatedAt, DraftableOutput);
        var newer = await EnqueueDetailAsync("public-law-history");
        var detail = await AdmitDetailAsync(newer, DetailBill(UpdatedAt.AddDays(1), cosponsors: 0),
            BillGateOutcome.Passed(BillGateReason.CoveredSponsor));
        var actions = detail.Jobs.Single(job => job.Url.Contains("/actions?")).JobId;
        await AdmitActionsAsync(actions, new CongressBillActionsPage([
            HistoryAction("Became Public Law No: 119-21."), HistoryAction("Became Public Law No: 119-99. More text.")], null));
        var capture = Assert.Single(await ReadBillInputsAsync([RecordId])).History!;
        await PostgresPublicationStore.FromConnectionString(connectionString, TestCodeVersion.Current)
            .PrepareBillHistoriesAsync([capture], default);
        Assert.Equal("119-21", await ScalarWithParametersAsync("SELECT public_law FROM published_bill_histories WHERE detail_job_id = @detail",
            new NpgsqlParameter("detail", newer)));
    }

    [Fact]
    public async Task AnInterruptedBodyInsertLeavesNoParentOrPartialActionsAndCanBeRetried()
    {
        await DraftForReviewAsync(UpdatedAt, DraftableOutput);
        var newer = await EnqueueDetailAsync("interrupted-history");
        var detail = await AdmitDetailAsync(newer, DetailBill(UpdatedAt.AddDays(1), cosponsors: 0),
            BillGateOutcome.Passed(BillGateReason.CoveredSponsor));
        var actions = detail.Jobs.Single(job => job.Url.Contains("/actions?")).JobId;
        await AdmitActionsAsync(actions, new CongressBillActionsPage([HistoryAction("First."), HistoryAction("Second.")], null));
        var capture = Assert.Single(await ReadBillInputsAsync([RecordId])).History!;
        var publications = PostgresPublicationStore.FromConnectionString(connectionString, TestCodeVersion.Current);
        await ExecuteAsync(connectionString, """
            CREATE FUNCTION fail_history_copy() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
              IF NEW.position = 1 THEN RAISE EXCEPTION 'Interrupted history copy'; END IF;
              RETURN NEW;
            END $$;
            CREATE TRIGGER fail_history_copy BEFORE INSERT ON published_bill_history_actions
                FOR EACH ROW EXECUTE FUNCTION fail_history_copy();
            """);

        var error = await Assert.ThrowsAsync<PostgresException>(() => publications.PrepareBillHistoriesAsync([capture], default));
        Assert.Equal("P0001", error.SqlState);
        Assert.Equal(0L, await ScalarAsync("SELECT count(*) FROM published_bill_histories"));
        Assert.Equal(0L, await ScalarAsync("SELECT count(*) FROM published_bill_history_actions"));
        await ExecuteAsync(connectionString, "DROP TRIGGER fail_history_copy ON published_bill_history_actions; DROP FUNCTION fail_history_copy();");
        await publications.PrepareBillHistoriesAsync([capture], default);
        Assert.Equal(1L, await ScalarAsync("SELECT count(*) FROM published_bill_histories"));
        Assert.Equal(2L, await ScalarAsync("SELECT count(*) FROM published_bill_history_actions"));
    }

    [Fact]
    public async Task ReusingSeveralCopiesBatchesTheirMetadataAndReadsNoOperationalBodies()
    {
        await DraftForReviewAsync(UpdatedAt, DraftableOutput);
        var first = Assert.Single(await ReadBillInputsAsync([RecordId])).History!;
        var newer = await EnqueueDetailAsync("second-copy");
        var detail = await AdmitDetailAsync(newer, DetailBill(UpdatedAt.AddDays(1), cosponsors: 0),
            BillGateOutcome.Passed(BillGateReason.CoveredSponsor));
        await AdmitActionsAsync(detail.Jobs.Single(job => job.Url.Contains("/actions?")).JobId,
            new CongressBillActionsPage([HistoryAction("Another capture.")], null));
        var second = Assert.Single(await ReadBillInputsAsync([RecordId])).History!;
        var counter = new HistoryReadCounter();
        var options = new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString).AddInterceptors(counter).Options;
        var publications = new PostgresPublicationStore(new PooledDbContextFactory<CollectionAttemptDbContext>(options), TestCodeVersion.Current);
        await publications.PrepareBillHistoriesAsync([first, second], default);
        counter.Commands.Clear();

        await publications.PrepareBillHistoriesAsync([first, second], default);

        Assert.Single(counter.Commands);
        Assert.Contains("published_bill_histories", counter.Commands[0], StringComparison.Ordinal);
        Assert.DoesNotContain("bill_actions", counter.Commands[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task AFinalCommitRejectsMissingOrMisboundCopiesBeforeWritingAnyRelease()
    {
        await DraftForReviewAsync(UpdatedAt, DraftableOutput);
        var capture = Assert.Single(await ReadBillInputsAsync([RecordId])).History!;
        var publications = PostgresPublicationStore.FromConnectionString(connectionString, TestCodeVersion.Current);
        await publications.PrepareBillHistoriesAsync([capture], default);
        var commit = new PublicationCommit(1, null, $"000001-{Guid.NewGuid():N}",
            [new(CivicLens.Publication.Contracts.PublishedRecordKind.Action, RecordId, 1)], [])
        {
            Names = new([], new([], [])),
            Withdrawals = [],
            BillHistories = [new(RecordId, "missing-capture")]
        };
        await Assert.ThrowsAsync<PublicationConflictException>(() => publications.CommitAsync("owner", commit, "missing-history", new string('0', 64), default));
        await ExecuteAsync(connectionString, "UPDATE published_bill_histories SET bill_number = bill_number + 1");
        commit = commit with { BillHistories = [new(RecordId, capture.DetailJobId)] };
        await Assert.ThrowsAsync<PublicationConflictException>(() => publications.CommitAsync("owner", commit, "misbound-history", new string('1', 64), default));
        Assert.Equal(0L, await ScalarAsync("SELECT count(*) FROM publication_releases"));
        Assert.Equal(0L, await ScalarAsync("SELECT count(*) FROM release_bill_histories"));
        await ExecuteAsync(connectionString, "UPDATE published_bill_histories SET bill_number = bill_number - 1");
        await publications.CommitAsync("owner", commit, "valid-history", new string('2', 64), default);
        Assert.Equal(1L, await ScalarAsync("SELECT count(*) FROM release_bill_histories"));
    }

    [Fact]
    public async Task ConcurrentPreparationReportsAConflictAndTheLosingPublicationCanReuseTheCompletedCopy()
    {
        await DraftForReviewAsync(UpdatedAt, DraftableOutput);
        var capture = Assert.Single(await ReadBillInputsAsync([RecordId])).History!;
        var barrier = new HistoryPreparationBarrier();
        var options = new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString).AddInterceptors(barrier).Options;
        var publications = new PostgresPublicationStore(new PooledDbContextFactory<CollectionAttemptDbContext>(options), TestCodeVersion.Current);

        async Task<Exception?> PrepareAsync()
        {
            try { await publications.PrepareBillHistoriesAsync([capture], default); return null; }
            catch (PublicationConflictException error) { return error; }
        }
        var outcomes = await Task.WhenAll(PrepareAsync(), PrepareAsync());

        Assert.Single(outcomes, outcome => outcome is null);
        Assert.Single(outcomes, outcome => outcome is PublicationConflictException);
        Assert.Equal(1L, await ScalarAsync("SELECT count(*) FROM published_bill_histories"));
        await publications.PrepareBillHistoriesAsync([capture], default);
        Assert.Equal(1L, await ScalarAsync("SELECT count(*) FROM published_bill_histories"));
    }

    private sealed class HistoryPreparationBarrier : DbCommandInterceptor
    {
        private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int readers;

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context?.Database.CurrentTransaction is not null &&
                command.CommandText.Contains("published_bill_histories", StringComparison.Ordinal))
            {
                if (Interlocked.Increment(ref readers) == 2) ready.SetResult();
                await ready.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
            }
            return result;
        }
    }

    private sealed class HistoryReadCounter : DbCommandInterceptor
    {
        public List<string> Commands { get; } = [];

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Commands.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }

    private async Task<IReadOnlyList<BillPublicationInput>> ReadBillInputsAsync(ImmutableArray<string> recordIds) =>
        await PostgresPublicationStore.FromConnectionString(connectionString, TestCodeVersion.Current)
            .ReadBillInputsAsync(null, recordIds, default);

    private async Task<object?> ScalarWithParametersAsync(string sql, params NpgsqlParameter[] parameters)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddRange(parameters);
        return await command.ExecuteScalarAsync();
    }
}
