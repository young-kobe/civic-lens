using System.Text.Json.Serialization;

namespace CivicLens.PublicApi.Search;

public sealed record SearchPage(int Release, RecordHit[] Hits, string? NextCursor)
{
    /// <summary>The cursor of the previous page of the search page. The JSON route reads no previous page cursor, so its answer does not carry one.</summary>
    [JsonIgnore] public string? PreviousCursor { get; init; }
}
