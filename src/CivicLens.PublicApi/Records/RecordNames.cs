using System.Data;
using CivicLens.PublicWeb.Publication;
using Npgsql;

namespace CivicLens.PublicApi.Records;

/// <summary>Reads for the statement and document change pages. It opens the read-only REPEATABLE READ transaction that one page reads inside, so every read of the page comes from one snapshot and one release.
/// It then reads the names that the page shows for its officials and issues. Both name reads go out as one batch. Order follows the order of the IDs.</summary>
public sealed class RecordNames(NpgsqlDataSource views)
{
    public async Task<(NpgsqlConnection Connection, NpgsqlTransaction Transaction)> OpenSnapshotAsync(CancellationToken cancellationToken)
    {
        var connection = await views.OpenConnectionAsync(cancellationToken);
        try
        {
            var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
            await using var readOnly = new NpgsqlCommand("SET TRANSACTION READ ONLY", connection, transaction);
            await readOnly.ExecuteNonQueryAsync(cancellationToken);
            return (connection, transaction);
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    public async Task<(RecordOfficialData[] Officials, RecordIssueData[] Issues)> ReadAsync(NpgsqlTransaction transaction,
        string[] officialIds, string[] issueIds, CancellationToken cancellationToken)
    {
        if (officialIds.Length == 0 && issueIds.Length == 0) return ([], []);
        await using var batch = new NpgsqlBatch(transaction.Connection, transaction);
        var officials = new NpgsqlBatchCommand("SELECT o.official_id, o.name FROM public_api.officials o WHERE o.official_id = ANY(@ids)");
        officials.Parameters.AddWithValue("ids", officialIds);
        batch.BatchCommands.Add(officials);
        var issues = new NpgsqlBatchCommand("SELECT i.issue_id, i.name FROM public_api.release_issues i WHERE i.issue_id = ANY(@ids)");
        issues.Parameters.AddWithValue("ids", issueIds);
        batch.BatchCommands.Add(issues);
        await using var reader = await batch.ExecuteReaderAsync(cancellationToken);
        var officialNames = await ReadNamesAsync(reader, cancellationToken);
        await reader.NextResultAsync(cancellationToken);
        var issueNames = await ReadNamesAsync(reader, cancellationToken);
        return ([.. officialIds.Select(id => new RecordOfficialData(id,
                officialNames.TryGetValue(id, out var name) ? name : throw new InvalidDataException("An official of a published record has no name.")))],
            [.. issueIds.Where(issueNames.ContainsKey).Select(id => new RecordIssueData(id, issueNames[id]))]);
    }

    private static async Task<Dictionary<string, string>> ReadNamesAsync(NpgsqlDataReader reader, CancellationToken cancellationToken)
    {
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        while (await reader.ReadAsync(cancellationToken)) names[reader.GetString(0)] = reader.GetString(1);
        return names;
    }
}
