using System.Text.Json;
using CivicLens.Publication.Contracts;
using Npgsql;
using NpgsqlTypes;

namespace CivicLens.PublicApi.Ingest;

/// <summary>The writes of one release ingest. One transaction holds them all, and an advisory lock keeps a second ingest out.</summary>
public sealed class IndexTransaction : IAsyncDisposable
{
    private const long LockKey = 7_302_002;

    private readonly NpgsqlConnection connection;
    private readonly NpgsqlTransaction transaction;

    private IndexTransaction(NpgsqlConnection connection, NpgsqlTransaction transaction)
    {
        this.connection = connection;
        this.transaction = transaction;
    }

    public static async Task<IndexTransaction> BeginAsync(NpgsqlDataSource database, CancellationToken cancellationToken)
    {
        var connection = await database.OpenConnectionAsync(cancellationToken);
        var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var index = new IndexTransaction(connection, transaction);
        await using var command = index.Command("SELECT pg_advisory_xact_lock(@key)", ("key", LockKey));
        await command.ExecuteNonQueryAsync(cancellationToken);
        return index;
    }

    public async Task<int?> LatestReleaseAsync(CancellationToken cancellationToken)
    {
        await using var command = Command("SELECT max(release_number) FROM releases");
        return await command.ExecuteScalarAsync(cancellationToken) is int latest ? latest : null;
    }

    /// <summary>Every stored revision. The value tells whether the revision is still current.</summary>
    public async Task<Dictionary<(string RecordId, int Revision), bool>> StoredRevisionsAsync(CancellationToken cancellationToken)
    {
        await using var command = Command("SELECT record_id, revision_number, last_release IS NULL FROM records");
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var stored = new Dictionary<(string, int), bool>();
        while (await reader.ReadAsync(cancellationToken)) stored[(reader.GetString(0), reader.GetInt32(1))] = reader.GetBoolean(2);
        return stored;
    }

    public async Task AddReleaseAsync(PublicationRelease release, CancellationToken cancellationToken)
    {
        await using var command = Command("INSERT INTO releases (release_number, published_at_utc) VALUES (@number, @published)",
            ("number", release.ReleaseNumber), ("published", release.PublishedAtUtc.UtcDateTime));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task InsertAsync(IndexedRecord record, int firstRelease, CancellationToken cancellationToken)
    {
        var entry = record.Entry;
        await using var command = Command("""
            INSERT INTO records (record_id, revision_number, kind, headline, first_published_at_utc, officials, official_ids,
                issue_ids, policy_area_ids, source_host, event_date, body, search, first_release)
            VALUES (@id, @revision, @kind, @headline, @first, @officials, @official_ids, @issue_ids, @policy_area_ids, @host, @date, @body,
                setweight(to_tsvector('english', @headline), 'A') ||
                setweight(to_tsvector('english', @summary), 'B') ||
                setweight(to_tsvector('english', @source), 'C'), @release)
            """,
            ("id", entry.RecordId), ("revision", entry.RevisionNumber), ("kind", RecordKinds.Text(entry.Kind)),
            ("headline", entry.Headline), ("first", entry.FirstPublishedAtUtc.UtcDateTime),
            ("official_ids", entry.Officials.Select(official => official.Id).ToArray()),
            ("issue_ids", entry.IssueIds), ("policy_area_ids", entry.PolicyAreaIds),
            ("body", record.Body), ("summary", record.Summary), ("source", record.Source), ("release", firstRelease));
        command.Parameters.Add(new NpgsqlParameter("officials", NpgsqlDbType.Jsonb)
        {
            Value = JsonSerializer.Serialize(entry.Officials, PublicationProtocol.JsonOptions)
        });
        command.Parameters.Add(new NpgsqlParameter("host", NpgsqlDbType.Text) { Value = (object?)entry.SourceHost ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("date", NpgsqlDbType.Date) { Value = (object?)entry.EventDate ?? DBNull.Value });
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>Ends the range of each revision after the given last release.</summary>
    public Task CloseAsync(IReadOnlyCollection<(string RecordId, int Revision)> revisions, int lastRelease, CancellationToken cancellationToken) =>
        SetLastReleaseAsync(revisions, lastRelease, cancellationToken);

    /// <summary>Makes each revision current again, because a release holds it after a release dropped it.</summary>
    public Task ReopenAsync(IReadOnlyCollection<(string RecordId, int Revision)> revisions, CancellationToken cancellationToken) =>
        SetLastReleaseAsync(revisions, null, cancellationToken);

    public Task CommitAsync(CancellationToken cancellationToken) => transaction.CommitAsync(cancellationToken);

    public async ValueTask DisposeAsync()
    {
        await transaction.DisposeAsync();
        await connection.DisposeAsync();
    }

    private async Task SetLastReleaseAsync(IReadOnlyCollection<(string RecordId, int Revision)> revisions, int? lastRelease,
        CancellationToken cancellationToken)
    {
        if (revisions.Count == 0) return;
        await using var command = Command("""
            UPDATE records SET last_release = @last
            WHERE (record_id, revision_number) IN (SELECT * FROM unnest(@ids, @revisions))
            """,
            ("ids", revisions.Select(revision => revision.RecordId).ToArray()),
            ("revisions", revisions.Select(revision => revision.Revision).ToArray()));
        command.Parameters.Add(new NpgsqlParameter("last", NpgsqlDbType.Integer) { Value = (object?)lastRelease ?? DBNull.Value });
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private NpgsqlCommand Command(string sql, params (string Name, object Value)[] parameters)
    {
        var command = new NpgsqlCommand(sql, connection, transaction);
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
        return command;
    }
}
