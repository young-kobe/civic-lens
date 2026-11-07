using System.Net;
using System.Text.Json;
using CivicLens.Tests.Infrastructure.Collection;
using CivicLens.Tests.PublicApi.Votes;
using Npgsql;

namespace CivicLens.Tests.PublicApi.Bills;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class ActionHistoryEndpointsTests(PostgresCollection postgres)
{
    private static readonly string RecordId = new('a', 64);
    private static readonly DateTimeOffset Observed = new(2026, 3, 4, 10, 20, 30, TimeSpan.Zero);

    [Fact]
    public async Task PagesKeepTheSnapshotMetadataOrderAndNullableOfficialFieldsAndRejectAChangedRelease()
    {
        await using var host = await StartAsync();
        await SeedHistoryAsync(host.OperationalDatabase, 'a', "captured-actions", 3);
        using var first = await ReadAsync(host, $"/v1/bills/{RecordId}/actions?limit=2");
        Assert.Equal(1, first.RootElement.GetProperty("release").GetInt32());
        Assert.Equal(Observed, first.RootElement.GetProperty("history").GetProperty("observedAtUtc").GetDateTimeOffset());
        Assert.Equal(3, first.RootElement.GetProperty("history").GetProperty("actionCount").GetInt32());
        var rows = first.RootElement.GetProperty("actions").EnumerateArray().ToArray();
        Assert.Equal([0, 1], rows.Select(row => row.GetProperty("position").GetInt32()));
        Assert.Equal("2025-03-02", rows[0].GetProperty("actionDate").GetString());
        Assert.Equal("13:45:10", rows[0].GetProperty("actionTime").GetString());
        Assert.Equal("Floor", rows[0].GetProperty("type").GetString());
        Assert.Equal("H8D000", rows[0].GetProperty("actionCode").GetString());
        Assert.Equal(2, rows[0].GetProperty("sourceSystemCode").GetInt32());
        Assert.Equal("House floor actions", rows[0].GetProperty("sourceSystemName").GetString());
        Assert.Equal(JsonValueKind.Null, rows[1].GetProperty("actionTime").ValueKind);
        Assert.Equal(JsonValueKind.Null, rows[1].GetProperty("sourceSystemCode").ValueKind);
        var cursor = first.RootElement.GetProperty("nextCursor").GetString()!;
        using var next = await ReadAsync(host, $"/v1/bills/{RecordId}/actions?limit=2&cursor={Uri.EscapeDataString(cursor)}");
        Assert.Equal([2], next.RootElement.GetProperty("actions").EnumerateArray().Select(row => row.GetProperty("position").GetInt32()));
        Assert.Equal(JsonValueKind.Null, next.RootElement.GetProperty("nextCursor").ValueKind);
        using var detail = await ReadAsync(host, $"/v1/bills/{RecordId}");
        Assert.Equal("119-7", detail.RootElement.GetProperty("bill").GetProperty("publicLaw").GetString());
        Assert.Equal(3, detail.RootElement.GetProperty("history").GetProperty("actionCount").GetInt32());

        await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 2, Observed.AddDays(1));
        using var stale = await host.Client.GetAsync($"/v1/bills/{RecordId}/actions?cursor={Uri.EscapeDataString(cursor)}");
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        using var current = await ReadAsync(host, $"/v1/bills/{RecordId}/actions");
        Assert.Equal(JsonValueKind.Null, current.RootElement.GetProperty("history").ValueKind);
        Assert.Empty(current.RootElement.GetProperty("actions").EnumerateArray());
        await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 1, Observed);
        using var restored = await ReadAsync(host, $"/v1/bills/{RecordId}/actions");
        Assert.Equal(3, restored.RootElement.GetProperty("actions").GetArrayLength());
    }

    [Fact]
    public async Task CompletedEmptyHistoryDiffersFromNoPublishedHistoryAndMissingBillsKeepTheExisting404Precedence()
    {
        await using var host = await StartAsync();
        await SeedHistoryAsync(host.OperationalDatabase, 'b', "empty-actions", 0);
        using var absent = await ReadAsync(host, $"/v1/bills/{RecordId}/actions");
        Assert.Equal(JsonValueKind.Null, absent.RootElement.GetProperty("history").ValueKind);
        Assert.Empty(absent.RootElement.GetProperty("actions").EnumerateArray());
        using var empty = await ReadAsync(host, $"/v1/bills/{new string('b', 64)}/actions");
        Assert.Equal(0, empty.RootElement.GetProperty("history").GetProperty("actionCount").GetInt32());
        Assert.Empty(empty.RootElement.GetProperty("actions").EnumerateArray());
        using var unknown = await host.Client.GetAsync($"/v1/bills/{new string('f', 64)}/actions?cursor=ba.999.0");
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        using var invalid = await host.Client.GetAsync("/v1/bills/invalid/actions");
        Assert.Equal(HttpStatusCode.NotFound, invalid.StatusCode);
        await ViewsSeed.ExecuteAsync(host.OperationalDatabase, "UPDATE publication_releases SET is_active = false");
        using var noRelease = await host.Client.GetAsync($"/v1/bills/{RecordId}/actions");
        Assert.Equal(HttpStatusCode.NotFound, noRelease.StatusCode);
    }

    private async Task<ApiHost> StartAsync()
    {
        var host = await ApiHost.StartAsync(postgres, new FakeReleaseSite());
        await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 1, Observed);
        await BillsSeed.AddBillsAsync(host.OperationalDatabase, [new('a', "2026-03-02"), new('b', "2026-03-02")]);
        return host;
    }

    private static async Task<JsonDocument> ReadAsync(ApiHost host, string path)
    {
        using var response = await host.Client.GetAsync(path);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{path}: {(int)response.StatusCode} {body}");
        return JsonDocument.Parse(body);
    }

    private static async Task SeedHistoryAsync(string connectionString, char record, string capture, int count)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            SET session_replication_role = replica;
            INSERT INTO published_bill_histories
                (detail_job_id, source_id, congress, bill_type, bill_number, observed_at_utc_ticks, action_count, public_law)
            VALUES (@capture, 'congress', 119, 'hr', 7, @observed, @count, '119-7');
            INSERT INTO published_bill_history_actions
                (detail_job_id, position, action_date, action_time, text, type, action_code, source_system_code, source_system_name)
            SELECT @capture, p, DATE '2025-03-02', CASE WHEN p = 0 THEN TIME '13:45:10' END, 'Official action ' || p,
                   CASE WHEN p = 0 THEN 'Floor' END, CASE WHEN p = 0 THEN 'H8D000' END,
                   CASE WHEN p = 0 THEN 2 END, CASE WHEN p = 0 THEN 'House floor actions' END
              FROM generate_series(0, @count - 1) p;
            INSERT INTO release_bill_histories (release_number, record_id, detail_job_id) VALUES (1, @record, @capture);
            RESET session_replication_role;
            """, connection);
        command.Parameters.AddWithValue("record", new string(record, 64));
        command.Parameters.AddWithValue("capture", capture);
        command.Parameters.AddWithValue("observed", Observed.UtcTicks);
        command.Parameters.AddWithValue("count", count);
        await command.ExecuteNonQueryAsync();
    }
}
