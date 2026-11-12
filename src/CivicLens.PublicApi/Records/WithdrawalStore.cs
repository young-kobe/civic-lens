using CivicLens.Publication.Contracts;
using Npgsql;

namespace CivicLens.PublicApi.Records;

/// <summary>Reads one withdrawal and its correction history from the active release's public views.</summary>
public sealed class WithdrawalStore(NpgsqlDataSource views)
{
    private const string Select = """
        SELECT w.release_number, w.kind, w.record_id, w.withdrawn_revision_number,
               w.first_published_at_utc_ticks, w.withdrawn_at_utc_ticks, w.note,
               c."position", c.replaced_revision_number, c.published_at_utc_ticks, c.note, c.facts_unchanged
          FROM public_api.withdrawals w
          LEFT JOIN public_api.withdrawal_corrections c
            ON c.release_number = w.release_number AND c.kind = w.kind AND c.record_id = w.record_id AND c.first_release = w.first_release
        """;

    private const string ByKindSql = Select + """
         WHERE w.kind = @kind AND w.record_id = @id
         ORDER BY c."position"
        """;

    private const string ByRecordIdSql = Select + """
         WHERE w.record_id = @id
         ORDER BY w.kind, c."position"
        """;

    /// <summary>Returns the active release's notice. Null means no release or no current withdrawal for the record.</summary>
    public async Task<WithdrawalDetail?> FindAsync(PublishedRecordKind kind, string recordId, CancellationToken cancellationToken)
    {
        await using var command = views.CreateCommand(ByKindSql);
        command.Parameters.AddWithValue("kind", PublicationProtocol.KindText(kind));
        command.Parameters.AddWithValue("id", recordId);
        return await ReadAsync(command, kind, cancellationToken);
    }

    /// <summary>Returns the active release's notice for a record ID alone, because a record page route does not know the kind of a withdrawn record. When the ID is withdrawn under several kinds, the first kind in text order answers.</summary>
    public async Task<WithdrawalDetail?> FindAsync(string recordId, CancellationToken cancellationToken)
    {
        await using var command = views.CreateCommand(ByRecordIdSql);
        command.Parameters.AddWithValue("id", recordId);
        return await ReadAsync(command, null, cancellationToken);
    }

    private static async Task<WithdrawalDetail?> ReadAsync(NpgsqlCommand command, PublishedRecordKind? kind, CancellationToken cancellationToken)
    {
        int? release = null;
        PublishedWithdrawal? withdrawal = null;
        var corrections = new List<PublishedCorrection>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (!RecordKinds.TryParse(reader.GetString(1), out var rowKind)) throw new InvalidDataException("The view holds an unknown record kind.");
            kind ??= rowKind;
            if (rowKind != kind) continue;
            release = reader.GetInt32(0);
            withdrawal ??= new PublishedWithdrawal
            {
                Kind = kind.Value,
                RecordId = reader.GetString(2),
                WithdrawnRevisionNumber = reader.GetInt32(3),
                FirstPublishedAtUtc = Utc(reader.GetInt64(4)),
                WithdrawnAtUtc = Utc(reader.GetInt64(5)),
                Note = reader.GetString(6),
                Corrections = []
            };
            if (!reader.IsDBNull(7))
                corrections.Add(new PublishedCorrection
                {
                    ReplacedRevisionNumber = reader.GetInt32(8),
                    PublishedAtUtc = Utc(reader.GetInt64(9)),
                    Note = reader.IsDBNull(10) ? null : reader.GetString(10),
                    FactsUnchanged = reader.GetBoolean(11)
                });
        }
        if (withdrawal is null || release is null) return null;
        withdrawal = withdrawal with { Corrections = [.. corrections] };
        withdrawal.Validate();
        return new(release.Value, withdrawal);
    }

    private static DateTimeOffset Utc(long ticks) => new(ticks, TimeSpan.Zero);
}
