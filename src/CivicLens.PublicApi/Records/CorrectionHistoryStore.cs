using CivicLens.Publication.Contracts;
using Npgsql;

namespace CivicLens.PublicApi.Records;

/// <summary>Reads a record's active published correction history from the restricted public views.</summary>
public sealed class CorrectionHistoryStore(NpgsqlDataSource views)
{
    private const string Sql = """
        SELECT a.release_number, r.kind, r.record_id, r.revision_number,
               h.history_id, h.encoding_version, h.correction_count,
               e."position", e.replaced_revision_number, e.published_at_utc_ticks,
               e.published_offset_minutes, e.note, e.facts_unchanged
          FROM (SELECT release_number FROM public_api.active_release LIMIT 1) a
          LEFT JOIN public_api.active_records r
            ON r.release_number = a.release_number AND r.kind = @kind AND r.record_id = @id
          LEFT JOIN public_api.record_correction_histories h
            ON h.release_number = a.release_number AND h.kind = r.kind AND h.record_id = r.record_id AND h.revision_number = r.revision_number
          LEFT JOIN public_api.record_correction_history_entries e
            ON e.release_number = h.release_number AND e.kind = h.kind AND e.record_id = h.record_id
               AND e.revision_number = h.revision_number
         ORDER BY e."position"
         LIMIT @take
        """;

    public async Task<(int? Release, CorrectionHistoryDetail? Detail)> FindAsync(PublishedRecordKind kind, string recordId, CancellationToken cancellationToken)
    {
        await using var command = views.CreateCommand(Sql);
        command.Parameters.AddWithValue("kind", PublicationProtocol.KindText(kind));
        command.Parameters.AddWithValue("id", recordId);
        command.Parameters.AddWithValue("take", PublishedCorrection.MaximumEntries + 1);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken) || reader.IsDBNull(0)) return (null, null);
        var release = reader.GetInt32(0);
        if (reader.IsDBNull(1)) return (release, null);

        ValidateRecordIdentity(reader, release, kind, recordId);
        var revisionNumber = ReadRevisionNumber(reader);
        if (revisionNumber < 1) throw new InvalidDataException("The active record has an invalid revision number.");
        var header = ReadHeader(reader);
        ValidateHeader(header);

        var corrections = new List<PublishedCorrection>(header.CorrectionCount);
        var rowCount = 1;
        ReadEntry(reader, corrections);
        while (await reader.ReadAsync(cancellationToken))
        {
            rowCount++;
            if (rowCount > PublishedCorrection.MaximumEntries + 1)
                throw new InvalidDataException("The active record correction history exceeds its maximum length.");
            ValidateRecordIdentity(reader, release, kind, recordId);
            if (ReadRevisionNumber(reader) != revisionNumber || ReadHeader(reader) != header)
                throw new InvalidDataException("The active record correction history has duplicate or inconsistent headers.");
            ReadEntry(reader, corrections);
        }

        if (corrections.Count != header.CorrectionCount || rowCount != Math.Max(1, (int)header.CorrectionCount))
            throw new InvalidDataException("The active record correction history count does not match its entries.");
        var items = corrections.ToArray();
        PublishedCorrection.ValidateAll(items, revisionNumber, kind);
        if (!string.Equals(PublishedCorrectionHistoryIdentity.Of(items, header.EncodingVersion), header.HistoryId, StringComparison.Ordinal))
            throw new InvalidDataException("The active record correction history identity does not match its entries.");
        return (release, new(release, kind, recordId, revisionNumber, items));
    }

    private static void ValidateRecordIdentity(NpgsqlDataReader reader, int release, PublishedRecordKind kind, string recordId)
    {
        if (reader.GetInt32(0) != release || reader.IsDBNull(1) || reader.IsDBNull(2) ||
            reader.GetString(1) != PublicationProtocol.KindText(kind) || reader.GetString(2) != recordId)
            throw new InvalidDataException("The active record view returned a different record identity.");
    }

    private static int ReadRevisionNumber(NpgsqlDataReader reader) => reader.IsDBNull(3)
        ? throw new InvalidDataException("The active record has no revision number.")
        : reader.GetInt32(3);

    private static Header ReadHeader(NpgsqlDataReader reader)
    {
        if (reader.IsDBNull(4) || reader.IsDBNull(5) || reader.IsDBNull(6))
            throw new InvalidDataException("The active record has an incomplete correction history header.");
        return new(reader.GetString(4), reader.GetInt32(5), reader.GetInt16(6));
    }

    private static void ValidateHeader(Header header)
    {
        if (header.EncodingVersion != PublishedCorrectionHistoryIdentity.CurrentVersion ||
            header.CorrectionCount < 0 || header.CorrectionCount > PublishedCorrection.MaximumEntries)
            throw new InvalidDataException("The active record correction history header is invalid.");
    }

    private static void ReadEntry(NpgsqlDataReader reader, List<PublishedCorrection> corrections)
    {
        if (reader.IsDBNull(7)) return;
        if (reader.GetInt16(7) != corrections.Count)
            throw new InvalidDataException("The active record correction history positions are invalid.");
        try
        {
            var timestamp = new DateTimeOffset(reader.GetInt64(9), TimeSpan.Zero).ToOffset(TimeSpan.FromMinutes(reader.GetInt16(10)));
            corrections.Add(new PublishedCorrection
            {
                ReplacedRevisionNumber = reader.GetInt32(8),
                PublishedAtUtc = timestamp,
                Note = reader.IsDBNull(11) ? null : reader.GetString(11),
                FactsUnchanged = reader.GetBoolean(12)
            });
        }
        catch (ArgumentOutOfRangeException exception)
        {
            throw new InvalidDataException("The active record correction history contains an invalid timestamp.", exception);
        }
    }

    private readonly record struct Header(string HistoryId, int EncodingVersion, short CorrectionCount);
}
