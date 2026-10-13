using CivicLens.Application.Collection;
using CivicLens.Collection.Contracts;

namespace CivicLens.Host.Collection;

public static class SourceLabels
{
    public static string Name(CollectionConfiguration? configuration, string sourceId)
    {
        var source = configuration?.Sources.FirstOrDefault(item => item.Id == sourceId);
        if (source is null) return sourceId;
        var officials = Officials(configuration!, source);
        return officials.Length == 0 ? Document(source.Url) : officials;
    }

    public static string Officials(CollectionConfiguration configuration, WatchedSourceConfiguration source)
    {
        var listed = configuration.ListedPersonIdsOf(source);
        return string.Join(", ", configuration.People.Where(person => listed.Contains(person.Id)).Select(person => person.Name));
    }

    public static string Document(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed)) return url;
        var segment = parsed.AbsolutePath.Trim('/').Split('/')[^1];
        return segment.Length == 0 ? parsed.Host : Uri.UnescapeDataString(segment).Replace('-', ' ');
    }

    public static string Location(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var parsed) ? parsed.Host + parsed.AbsolutePath : url;

    public static string Type(CollectionMode mode) => mode switch
    {
        CollectionMode.Html => "Newsroom",
        CollectionMode.Feed => "News feed",
        CollectionMode.Api => "API",
        CollectionMode.Page => "Tracked page",
        _ => throw new ArgumentOutOfRangeException(nameof(mode))
    };
}
