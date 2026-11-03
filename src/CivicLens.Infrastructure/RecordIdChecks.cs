namespace CivicLens.Infrastructure;

internal static class RecordIdChecks
{
    public static string AnyRecordId(string column) => $"{column} ~ '^([0-9a-f]{{32}}|[0-9a-f]{{64}})$'";
}
