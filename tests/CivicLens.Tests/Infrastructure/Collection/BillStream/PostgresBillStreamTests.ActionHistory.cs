using CivicLens.Application.Collection.Bills;
using CivicLens.Core.Votes;
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
