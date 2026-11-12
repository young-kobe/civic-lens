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
    private static readonly (string Text, SearchSortKey Key)[] Names =
        [("date", SearchSortKey.Date), ("published", SearchSortKey.Published), ("headline", SearchSortKey.Headline), ("kind", SearchSortKey.Kind)];

    public static bool TryParse(string value, out SearchSortKey key)
    {
        var index = Array.FindIndex(Names, name => name.Text == value);
        key = index < 0 ? default : Names[index].Key;
        return index >= 0;
    }

    /// <summary>The query text of a sort key, the inverse of <see cref="TryParse"/>.</summary>
    public static string Text(SearchSortKey key) => Names.Single(name => name.Key == key).Text;

    public static bool DefaultsToDescending(SearchSortKey key) => key is SearchSortKey.Date or SearchSortKey.Published;
}
