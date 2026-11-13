using Npgsql;

namespace CivicLens.PublicApi.Ingest;

/// <summary>The record revisions of one batch read, as two parallel arrays. Each query filters its view with <see cref="Filter"/>.</summary>
public sealed class RevisionKeys(IReadOnlyCollection<ListedRecord> records)
{
    public const string Filter = "(x.record_id, x.revision_number) IN (SELECT * FROM unnest(@ids, @revisions))";

    private readonly string[] ids = [.. records.Select(record => record.RecordId)];
    private readonly int[] revisions = [.. records.Select(record => record.RevisionNumber)];

    public NpgsqlCommand Command(NpgsqlTransaction snapshot, string sql)
    {
        var command = new NpgsqlCommand(sql, snapshot.Connection, snapshot);
        command.Parameters.AddWithValue("ids", ids);
        command.Parameters.AddWithValue("revisions", revisions);
        return command;
    }
}
