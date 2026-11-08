using System.Text;
using CivicLens.Application.Collection;
using CivicLens.Application.Collection.Bills;
using CivicLens.Application.Collection.Processing;
using CivicLens.Application.Review;
using CivicLens.Core.Review;
using CivicLens.Core.Votes;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Documents;
using CivicLens.Tests.Fixtures;
using Npgsql;

namespace CivicLens.Tests.Infrastructure.Collection.BillStream;

public sealed partial class PostgresBillStreamTests
{
    private static readonly CongressBillRecordedVote SenateRoll = new(Chamber.Senate, 117, 2, 70,
        new DateTimeOffset(2022, 3, 8, 22, 45, 5, TimeSpan.Zero),
        "https://www.senate.gov/legislative/LIS/roll_call_votes/vote1172/vote_117_2_00070.xml");

    private static CongressBillAction HistoryAction(string text, params CongressBillRecordedVote[] votes) =>
        new(new DateOnly(2025, 3, 1), null, text, null, null, null, null, votes);

    [Fact]
    public async Task AnActionsPageAdmissionWritesEachActionAndRecordedVoteAsColumns()
    {
        var bill = await StartPassedBillAsync(cosponsors: 0, summaries: 0);
        var timed = new CongressBillAction(new DateOnly(2025, 3, 2), new TimeOnly(13, 45, 10), "Passed House.", "Floor", "H8D000", 2,
            "House floor actions", [new CongressBillRecordedVote(Chamber.House, 119, 1, 190, null, null)]);

        await AdmitActionsAsync(bill.Actions, new CongressBillActionsPage(
            [HistoryAction("Introduced."), timed, HistoryAction("Passed Senate.", SenateRoll)], null));

        Assert.Equal(
            [("Introduced.", null, null, null, null, null), ("Passed House.", "13:45:10", "Floor", "H8D000", 2, "House floor actions"),
                ("Passed Senate.", null, null, null, null, null)],
            await ActionRowsAsync());
        Assert.Equal(
            [(1, 0, "House", 119, 1, 190, (long?)null, (string?)null),
                (2, 0, "Senate", 117, 2, 70, SenateRoll.VotedAt!.Value.UtcTicks, SenateRoll.Url)],
            await RecordedVoteRowsAsync());
    }

    [Fact]
    public async Task ALaterActionsPageContinuesTheActionPositionsOfTheChain()
    {
        var bill = await StartPassedBillAsync(cosponsors: 0, summaries: 0);
        var first = await AdmitActionsAsync(bill.Actions,
            new CongressBillActionsPage([HistoryAction("One."), HistoryAction("Two.")], ActionsNext(250)));
        var secondPageJob = first.Jobs.Single().JobId;

        await AdmitActionsAsync(secondPageJob, new CongressBillActionsPage([HistoryAction("Three.", SenateRoll)], null));

        Assert.Equal([(0, "One.", bill.Actions), (1, "Two.", bill.Actions), (2, "Three.", secondPageJob)], await ActionPositionsAsync());
        Assert.Equal(2, Assert.Single(await RecordedVoteRowsAsync()).ActionPosition);
    }

    [Fact]
    public async Task AnActionPositionGivesItsLineOnTheRenderedPageOfItsJob()
    {
        var bill = await StartPassedBillAsync(cosponsors: 0, summaries: 0);
        var firstPage = new CongressBillActionsPage([HistoryAction("One."), HistoryAction("Two.")], ActionsNext(250));
        var secondPage = new CongressBillActionsPage([HistoryAction("Three."), HistoryAction("Four.")], null);
        var first = await AdmitActionsAsync(bill.Actions, firstPage);
        await AdmitActionsAsync(first.Jobs.Single().JobId, secondPage);
        var item = new CongressBillListItem(119, "hr", 7, Backfill);
        var rendered = new Dictionary<string, string[]>
        {
            [bill.Actions] = CongressBillRenderer.RenderActions(item, firstPage).Split('\n'),
            [first.Jobs.Single().JobId] = CongressBillRenderer.RenderActions(item, secondPage).Split('\n')
        };
        var rows = await ActionPositionsAsync();

        foreach (var (position, text, jobId) in rows)
        {
            var lowestOfJob = rows.Where(row => row.JobId == jobId).Min(row => row.Position);
            Assert.Contains(text, rendered[jobId][1 + position - lowestOfJob]);
        }
    }

    [Fact]
    public async Task TwoVersionsOfOneBillKeepSeparateActionHistoriesThatBothStartAtPositionZero()
    {
        var first = await EnqueueDetailAsync("first");
        var second = await EnqueueDetailAsync("second");
        var firstJob = (await AdmitDetailAsync(first, DetailBill(UpdatedAt, cosponsors: 0), BillGateOutcome.Passed(BillGateReason.CoveredSponsor)))
            .Jobs.Single(job => job.Url.Contains("/actions?")).JobId;
        var secondJob = (await AdmitDetailAsync(second, DetailBill(UpdatedAt.AddSeconds(1), cosponsors: 0),
            BillGateOutcome.Passed(BillGateReason.CoveredSponsor))).Jobs.Single(job => job.Url.Contains("/actions?")).JobId;

        await AdmitActionsAsync(firstJob, new CongressBillActionsPage([HistoryAction("Old one."), HistoryAction("Old two.")], null));
        await AdmitActionsAsync(secondJob, new CongressBillActionsPage([HistoryAction("New one.")], null));

        var expected = new[] { (0, "Old one.", firstJob), (1, "Old two.", firstJob), (0, "New one.", secondJob) };
        Assert.Equivalent(expected, await ActionPositionsAsync("detail_job_id, position"));
    }

    [Fact]
    public async Task ReplayingAnActionsPageAdmissionWritesNoSecondRows()
    {
        var bill = await StartPassedBillAsync(cosponsors: 0, summaries: 0);
        var page = new CongressBillActionsPage([HistoryAction("One.", SenateRoll)], null);
        var extractionId = await ExtractionAsync(bill.Actions);

        await store.AdmitActionsPageAsync(Source(), bill.Actions, page, extractionId, default);
        await store.AdmitActionsPageAsync(Source(), bill.Actions, page, extractionId, default);

        Assert.Equal(1L, await ScalarAsync("SELECT count(*) FROM bill_actions"));
        Assert.Equal(1L, await ScalarAsync("SELECT count(*) FROM bill_action_recorded_votes"));
        Assert.Equal(1, await ScalarAsync("SELECT imported_count FROM bill_pages WHERE job_id = '" + bill.Actions + "'"));
    }

    [Fact]
    public async Task AnEmptyActionsPageIsMarkedVerifiedWithZeroImportedActions()
    {
        var bill = await StartPassedBillAsync(cosponsors: 0, summaries: 0);
        await AdmitActionsAsync(bill.Actions, new CongressBillActionsPage([], null));

        Assert.Equal(0, await ScalarAsync("SELECT imported_count FROM bill_pages WHERE job_id = '" + bill.Actions + "'"));
    }

    [Fact]
    public async Task AReplayWithConflictingTypedActionsIsRefusedWithoutChangingTheSavedRows()
    {
        var bill = await StartPassedBillAsync(cosponsors: 0, summaries: 0);
        var extractionId = await ExtractionAsync(bill.Actions);
        await store.AdmitActionsPageAsync(Source(), bill.Actions,
            new CongressBillActionsPage([HistoryAction("Original.")], null), extractionId, default);

        await Assert.ThrowsAsync<InvalidDataException>(() => store.AdmitActionsPageAsync(Source(), bill.Actions,
            new CongressBillActionsPage([HistoryAction("Conflicting.")], null), extractionId, default));

        Assert.Equal(["Original."], (await ActionRowsAsync()).Select(row => row.Text));
        Assert.Equal(1, await ScalarAsync("SELECT imported_count FROM bill_pages WHERE job_id = '" + bill.Actions + "'"));
    }

    [Fact]
    public async Task AReplayWithADifferentExtractionIdentityIsRefused()
    {
        var bill = await StartPassedBillAsync(cosponsors: 0, summaries: 0);
        await AdmitActionsAsync(bill.Actions, new CongressBillActionsPage([], null));
        var differentExtraction = await ExtractionAsync(bill.Actions, "other captured body", "other extraction", "extraction");

        await Assert.ThrowsAsync<InvalidDataException>(() => store.AdmitActionsPageAsync(Source(), bill.Actions,
            new CongressBillActionsPage([], null), differentExtraction, default));

        Assert.Equal(0, await ScalarAsync("SELECT imported_count FROM bill_pages WHERE job_id = '" + bill.Actions + "'"));
    }

    [Fact]
    public async Task RecoveryRebuildsTheWholeOrderedChainWhenANewerPageIsLegacyAndItsRowsAreMissing()
    {
        var bill = await StartPassedBillAsync(cosponsors: 0, summaries: 0);
        var firstPage = new CongressBillActionsPage([HistoryAction("First one."), HistoryAction("First two.")], ActionsNext(250));
        var next = await AdmitActionsAsync(bill.Actions, firstPage);
        var secondJob = Assert.Single(next.Jobs).JobId;
        var secondPage = new CongressBillActionsPage([HistoryAction("Second page.")], null);
        await AdmitActionsAsync(secondJob, secondPage);
        await ExecuteAsync(connectionString,
            $"UPDATE bill_pages SET imported_count = NULL WHERE job_id = '{secondJob}'; DELETE FROM bill_actions WHERE job_id = '{secondJob}'");
        var pages = await ((IBillActionsBackfillStore)store).ListActionPagesAsync(bill.Detail, default);

        await ((IBillActionsBackfillStore)store).RecordVerifiedActionChainAsync(bill.Detail,
            [new(pages[0], firstPage), new(pages[1], secondPage)], default);

        Assert.Equal([(0, "First one.", bill.Actions), (1, "First two.", bill.Actions), (2, "Second page.", secondJob)],
            await ActionPositionsAsync());
        Assert.Equal(new long?[] { 2L, 1L }, await ImportedCountsAsync(bill.Detail));
    }

    [Fact]
    public async Task NormalCollectionAdmitsLaterPageBehindLegacyUnverifiedPageForWholeChainRecovery()
    {
        var bill = await StartPassedBillAsync(cosponsors: 0, summaries: 0);
        var firstPage = new CongressBillActionsPage([HistoryAction("First page.")], ActionsNext(250));
        var next = await AdmitActionsAsync(bill.Actions, firstPage);
        var secondJob = Assert.Single(next.Jobs).JobId;
        await ExecuteAsync(connectionString,
            $"UPDATE bill_pages SET imported_count = NULL WHERE job_id = '{bill.Actions}'");
        var secondPage = new CongressBillActionsPage([HistoryAction("Second page.")], null);

        await AdmitActionsAsync(secondJob, secondPage);

        Assert.Equal(new long?[] { null, null }, await ImportedCountsAsync(bill.Detail));
        Assert.Equal([(0, "First page.", bill.Actions)], await ActionPositionsAsync());
        var backfill = (IBillActionsBackfillStore)store;
        var pages = await backfill.ListActionPagesAsync(bill.Detail, default);
        Assert.Equal(2, pages.Count);

        await backfill.RecordVerifiedActionChainAsync(bill.Detail,
            [new(pages[0], firstPage), new(pages[1], secondPage)], default);

        Assert.Equal([(0, "First page.", bill.Actions), (1, "Second page.", secondJob)], await ActionPositionsAsync());
        Assert.Equal(new long?[] { 1L, 1L }, await ImportedCountsAsync(bill.Detail));
    }

    [Fact]
    public async Task RecoveryRepairsPagePositionsToTheExactVerifiedChain()
    {
        var bill = await StartPassedBillAsync(cosponsors: 0, summaries: 0);
        var firstPage = new CongressBillActionsPage([HistoryAction("First.")], ActionsNext(250));
        var next = await AdmitActionsAsync(bill.Actions, firstPage);
        var secondJob = Assert.Single(next.Jobs).JobId;
        var secondPage = new CongressBillActionsPage([HistoryAction("Second.")], null);
        await AdmitActionsAsync(secondJob, secondPage);
        await ExecuteAsync(connectionString,
            $"UPDATE bill_actions SET position = 10 WHERE job_id = '{bill.Actions}'; UPDATE bill_actions SET position = 0 WHERE job_id = '{secondJob}'; UPDATE bill_actions SET position = 1 WHERE job_id = '{bill.Actions}'");
        var pages = await ((IBillActionsBackfillStore)store).ListActionPagesAsync(bill.Detail, default);

        await ((IBillActionsBackfillStore)store).RecordVerifiedActionChainAsync(bill.Detail,
            [new(pages[0], firstPage), new(pages[1], secondPage)], default);

        Assert.Equal([(0, "First.", bill.Actions), (1, "Second.", secondJob)], await ActionPositionsAsync());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RecoveryRechecksTheGateSourceAndFollowOnRootBeforeWriting(bool changeSource)
    {
        var bill = await StartPassedBillAsync(cosponsors: 0, summaries: 0);
        var page = new CongressBillActionsPage([HistoryAction("Recovered.")], null);
        await AdmitActionsAsync(bill.Actions, page);
        await ExecuteAsync(connectionString,
            $"DELETE FROM bill_actions WHERE detail_job_id = '{bill.Detail}'; UPDATE bill_pages SET imported_count = NULL WHERE job_id = '{bill.Actions}'");
        var backfill = (IBillActionsBackfillStore)store;
        var pages = await backfill.ListActionPagesAsync(bill.Detail, default);
        if (changeSource)
            await ExecuteAsync(connectionString, $"UPDATE bill_gates SET source_id = 'another-source' WHERE detail_job_id = '{bill.Detail}'");
        else
        {
            var other = await EnqueueDetailAsync("different-root");
            await AdmitDetailAsync(other, DetailBill(UpdatedAt.AddDays(1), 0), BillGateOutcome.Passed(BillGateReason.CoveredSponsor));
            await ExecuteAsync(connectionString,
                $"UPDATE bill_follow_on_jobs SET detail_job_id = '{other}' WHERE job_id = '{bill.Actions}'");
        }

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            backfill.RecordVerifiedActionChainAsync(bill.Detail, [new(Assert.Single(pages), page)], default));

        Assert.Equal(0L, await ScalarAsync($"SELECT count(*) FROM bill_actions WHERE detail_job_id = '{bill.Detail}'"));
        Assert.Equal(1L, await ScalarAsync($"SELECT count(*) FROM bill_pages WHERE job_id = '{bill.Actions}' AND imported_count IS NULL"));
    }

    [Fact]
    public async Task AnUnchangedImportMarkerRollsBackTheRecoveredRows()
    {
        var bill = await StartPassedBillAsync(cosponsors: 0, summaries: 0);
        var page = new CongressBillActionsPage([HistoryAction("Recovered.")], null);
        await AdmitActionsAsync(bill.Actions, page);
        await ExecuteAsync(connectionString,
            $"DELETE FROM bill_actions WHERE detail_job_id = '{bill.Detail}'; UPDATE bill_pages SET imported_count = NULL WHERE job_id = '{bill.Actions}'");
        var backfill = (IBillActionsBackfillStore)store;
        var pages = await backfill.ListActionPagesAsync(bill.Detail, default);
        await ExecuteAsync(connectionString, """
            CREATE FUNCTION keep_history_import_unverified() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
                NEW.imported_count := NULL;
                RETURN NEW;
            END $$;
            CREATE TRIGGER keep_history_import_unverified BEFORE UPDATE ON bill_pages
            FOR EACH ROW EXECUTE FUNCTION keep_history_import_unverified();
            """);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            backfill.RecordVerifiedActionChainAsync(bill.Detail, [new(Assert.Single(pages), page)], default));

        Assert.Equal(0L, await ScalarAsync($"SELECT count(*) FROM bill_actions WHERE detail_job_id = '{bill.Detail}'"));
        Assert.Equal(1L, await ScalarAsync($"SELECT count(*) FROM bill_pages WHERE job_id = '{bill.Actions}' AND imported_count IS NULL"));
    }

    [Fact]
    public async Task TheKeysetRecoveryScanAdvancesPastUnverifiedBillsWithoutChangingThem()
    {
        var first = await StartPassedBillAsync(cosponsors: 0, summaries: 0);
        var secondDetail = await EnqueueDetailAsync("second");
        var admitted = await AdmitDetailAsync(secondDetail, DetailBill(UpdatedAt.AddDays(1), 0),
            BillGateOutcome.Passed(BillGateReason.CoveredSponsor));
        var secondActions = admitted.Jobs.Single(job => job.Url.Contains("/actions?")).JobId;
        await AdmitActionsAsync(first.Actions, new CongressBillActionsPage([], null));
        await AdmitActionsAsync(secondActions, new CongressBillActionsPage([], null));
        await ExecuteAsync(connectionString,
            $"UPDATE bill_pages SET imported_count = NULL WHERE role = 'Actions' AND detail_job_id IN ('{first.Detail}', '{secondDetail}')");
        var backfill = (IBillActionsBackfillStore)store;

        var firstBatch = Assert.Single(await backfill.ListBillsWithUnverifiedActionsAsync(null, 1, default));
        var secondBatch = Assert.Single(await backfill.ListBillsWithUnverifiedActionsAsync(firstBatch, 1, default));
        var remaining = await backfill.ListBillsWithUnverifiedActionsAsync(secondBatch, 1, default);

        Assert.NotEqual(firstBatch, secondBatch);
        Assert.Empty(remaining);
        Assert.Equal(firstBatch, Assert.Single(await backfill.ListBillsWithUnverifiedActionsAsync(null, 1, default)));
    }

    [Fact]
    public async Task LegacyRowsAreReconstructedFromTheExactSavedRawPageCapture()
    {
        var bill = await StartPassedBillAsync(cosponsors: 0, summaries: 0);
        var body = ActionBillWorld.ActionsBody(["Recovered from capture."], null);
        var extractionId = await ReadableExtractionAsync(bill.Actions, ApiResource.BillActions, body);
        var job = await store.GetAsync(bill.Actions, default);
        var request = Assert.Single(job!.Attempts).Request;
        var parsed = CongressBillActionsParser.Parse(Encoding.UTF8.GetBytes(body), request, SourceUrl,
            new CongressBillListItem(119, "hr", 7, Backfill));
        await store.AdmitActionsPageAsync(Source(), bill.Actions, parsed, extractionId, default);
        await ExecuteAsync(connectionString,
            $"UPDATE bill_pages SET imported_count = NULL WHERE job_id = '{bill.Actions}'; DELETE FROM bill_action_recorded_votes WHERE detail_job_id = '{bill.Detail}'; DELETE FROM bill_actions WHERE detail_job_id = '{bill.Detail}'");
        var responses = new ApiResponseReader(PostgresCollectionAttemptStore.FromConnectionString(connectionString), new CaptureBodyReader());
        var backfill = new BackfillBillActions(store, store, extractions, responses, captureRoot);

        var result = await backfill.ExecuteAsync(new ReviewActor("auth0|owner", ReviewRole.Owner), 10);

        Assert.Equal(new BillActionsBackfillResult(1, 1, 0), result);
        Assert.Equal([(0, "Recovered from capture.", bill.Actions)], await ActionPositionsAsync());
        Assert.Equal(1, await ScalarAsync($"SELECT imported_count FROM bill_pages WHERE job_id = '{bill.Actions}'"));
    }

    private async Task<IReadOnlyList<long?>> ImportedCountsAsync(string detailJobId)
    {
        await using var reader = await ReadAsync($"SELECT imported_count FROM bill_pages WHERE detail_job_id = '{detailJobId}' AND role = 'Actions' ORDER BY page_order");
        var counts = new List<long?>();
        while (await reader.ReadAsync()) counts.Add(reader.IsDBNull(0) ? null : reader.GetInt32(0));
        return counts;
    }

    [Fact]
    public async Task ARejectedActionsPageWritesNoActionRows()
    {
        var bill = await StartPassedBillAsync(cosponsors: 0, summaries: 0);
        var foreign = await ExtractionAsync(await EnqueueDetailAsync("other"));

        await Assert.ThrowsAsync<InvalidDataException>(() => store.AdmitActionsPageAsync(Source(), bill.Actions,
            new CongressBillActionsPage([HistoryAction("One.", SenateRoll)], null), foreign, default));

        Assert.Equal(0L, await ScalarAsync("SELECT count(*) FROM bill_actions"));
        Assert.Equal(0L, await ScalarAsync("SELECT count(*) FROM bill_action_recorded_votes"));
    }

    private async Task<IReadOnlyList<(string Text, string? Time, string? Type, string? Code, int? SourceCode, string? SourceName)>>
        ActionRowsAsync()
    {
        await using var reader = await ReadAsync(
            "SELECT text, action_time::text, type, action_code, source_system_code, source_system_name FROM bill_actions ORDER BY position");
        var rows = new List<(string, string?, string?, string?, int?, string?)>();
        while (await reader.ReadAsync())
            rows.Add((reader.GetString(0), NullableString(reader, 1), NullableString(reader, 2), NullableString(reader, 3),
                reader.IsDBNull(4) ? null : reader.GetInt32(4), NullableString(reader, 5)));
        return rows;
    }

    private async Task<IReadOnlyList<(int ActionPosition, int Position, string Chamber, int Congress, int Session, int Roll,
        long? VotedAtTicks, string? Url)>> RecordedVoteRowsAsync()
    {
        await using var reader = await ReadAsync(
            "SELECT action_position, position, chamber, congress, session_number, roll_number, voted_at_utc_ticks, url " +
            "FROM bill_action_recorded_votes ORDER BY action_position, position");
        var rows = new List<(int, int, string, int, int, int, long?, string?)>();
        while (await reader.ReadAsync())
            rows.Add((reader.GetInt32(0), reader.GetInt32(1), reader.GetString(2), reader.GetInt32(3), reader.GetInt32(4),
                reader.GetInt32(5), reader.IsDBNull(6) ? null : reader.GetInt64(6), NullableString(reader, 7)));
        return rows;
    }

    private async Task<IReadOnlyList<(int Position, string Text, string JobId)>> ActionPositionsAsync(string order = "position")
    {
        await using var reader = await ReadAsync($"SELECT position, text, job_id FROM bill_actions ORDER BY {order}");
        var rows = new List<(int, string, string)>();
        while (await reader.ReadAsync()) rows.Add((reader.GetInt32(0), reader.GetString(1), reader.GetString(2)));
        return rows;
    }

    private async Task<NpgsqlDataReader> ReadAsync(string sql)
    {
        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        var command = new NpgsqlCommand(sql, connection);
        return await command.ExecuteReaderAsync(System.Data.CommandBehavior.CloseConnection);
    }

    private static string? NullableString(NpgsqlDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
}
