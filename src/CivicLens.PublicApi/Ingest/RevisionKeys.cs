using Npgsql;

namespace CivicLens.PublicApi.Ingest;

/// <summary>
/// The record revisions of one batch read, as two parallel arrays. Each query reads its view for each key with <see cref="Match"/>.
/// The keys drive the read, and each key finds its view rows in a correlated <c>LATERAL</c> subquery with <c>OFFSET 0</c>.
/// Postgres underestimates the rows of a view that reads <c>active_records</c>. A plain <c>IN</c> filter on the keys then scans the key list again for each view row.
/// One set holds at most <see cref="MaxCount"/> keys, so each statement stays inside the statement timeout of <c>civic_lens_public</c>.
/// </summary>
public sealed class RevisionKeys
{
    /// <summary>
    /// The most keys that one statement reads. On production, 500 action keys read the bills in 0.15 seconds and the action sources in 0.21 seconds.
    /// The role stops a statement after 2 seconds.
    /// </summary>
    public const int MaxCount = 500;

    private readonly string[] ids;
    private readonly int[] revisions;

    public RevisionKeys(IReadOnlyCollection<ListedRecord> records)
    {
        if (records.Count > MaxCount) throw new ArgumentException($"One statement reads at most {MaxCount} keys.", nameof(records));
        ids = [.. records.Select(record => record.RecordId)];
        revisions = [.. records.Select(record => record.RevisionNumber)];
    }

    /// <summary>
    /// Each query reads <c>FROM unnest(@ids, @revisions) k(record_id, revision_number)</c> and then its view in <c>CROSS JOIN LATERAL (SELECT * FROM view x WHERE Match OFFSET 0) x</c>.
    /// The view stays a literal name, so the boundary check of the service reads it. Postgres drops the view columns that the query does not read.
    /// </summary>
    public const string Match = "x.record_id = k.record_id AND x.revision_number = k.revision_number";

    public NpgsqlCommand Command(NpgsqlTransaction snapshot, string sql)
    {
        var command = new NpgsqlCommand(sql, snapshot.Connection, snapshot);
        command.Parameters.AddWithValue("ids", ids);
        command.Parameters.AddWithValue("revisions", revisions);
        return command;
    }
}
