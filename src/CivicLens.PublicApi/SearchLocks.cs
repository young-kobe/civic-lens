namespace CivicLens.PublicApi;

/// <summary>The advisory lock keys of the search database. The SQL hashes each name with <c>hashtextextended(name, 0)</c>.</summary>
public static class SearchLocks
{
    public const string Schema = "civic-lens-search-schema";
    public const string Ingest = "civic-lens-search-ingest";
}
