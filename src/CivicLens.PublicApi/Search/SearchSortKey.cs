namespace CivicLens.PublicApi.Search;

public enum SearchSortKey
{
    Date,
    Published,
    Headline,
    Kind
}

/// <summary>The names of the sort keys in the query string, and the direction that each key takes when the request names none.</summary>
public static class SearchSortKeys
{
    public static bool TryParse(string value, out SearchSortKey key)
    {
        (var found, key) = value switch
        {
            "date" => (true, SearchSortKey.Date),
            "published" => (true, SearchSortKey.Published),
            "headline" => (true, SearchSortKey.Headline),
            "kind" => (true, SearchSortKey.Kind),
            _ => (false, default)
        };
        return found;
    }

    public static bool DefaultsToDescending(SearchSortKey key) => key is SearchSortKey.Date or SearchSortKey.Published;
}
