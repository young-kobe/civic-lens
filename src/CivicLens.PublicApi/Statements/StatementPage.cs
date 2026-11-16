using System.Text.Json.Serialization;

namespace CivicLens.PublicApi.Statements;

public sealed record StatementPage(int Release, StatementRow[] Statements, string? NextCursor)
{
    /// <summary>The cursor of the previous page of the list page. The JSON route reads no previous page cursor, so its answer does not carry one.</summary>
    [JsonIgnore] public string? PreviousCursor { get; init; }
}
