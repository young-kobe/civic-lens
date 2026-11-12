using CivicLens.Publication.Contracts;
using Npgsql;

namespace CivicLens.PublicApi.Records;

/// <summary>Reads the kind of an active record from <c>public_api.active_records</c>. A record ID does not name its kind, so the one record page route asks here once and then calls the page reader of that kind.</summary>
public sealed class RecordKindStore(NpgsqlDataSource views)
{
    /// <summary>Reads the kind of one record. Null means that the active release holds no such record, or that the stored kind is unknown.</summary>
    public async Task<PublishedRecordKind?> FindAsync(string recordId, CancellationToken cancellationToken)
    {
        await using var command = views.CreateCommand("SELECT kind FROM public_api.active_records WHERE record_id = @id ORDER BY kind LIMIT 1");
        command.Parameters.AddWithValue("id", recordId);
        var text = await command.ExecuteScalarAsync(cancellationToken) as string;
        return RecordKinds.TryParse(text, out var kind) ? kind : null;
    }
}
