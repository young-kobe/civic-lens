namespace CivicLens.Infrastructure;

/// <summary>Owns the SQL text of the check that a column holds a record ID. It matches `PublicationProtocol.IsAnyRecordId`.</summary>
internal static class RecordIdChecks
{
    public static string AnyRecordId(string column) => $"{column} ~ '^([0-9a-f]{{32}}|[0-9a-f]{{64}})$'";
}
