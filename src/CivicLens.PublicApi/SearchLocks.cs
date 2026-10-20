namespace CivicLens.PublicApi;

/// <summary>
/// The advisory lock keys of the search database. The SQL hashes each name with <c>hashtextextended(name, 0)</c>.
/// No transaction holds two of these keys.
/// </summary>
public static class SearchLocks
{
    public const string Schema = "civic-lens-search-schema";
    public const string Ingest = "civic-lens-search-ingest";
    public const string Reports = "civic-lens-search-reports";

    /// <summary>Takes a key until the transaction ends. The statement takes the parameter <c>key</c>.</summary>
    public const string TakeTransactionLockSql = "SELECT pg_advisory_xact_lock(hashtextextended(@key, 0))";
}
