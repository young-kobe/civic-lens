using System.Text.Json.Serialization;

namespace CivicLens.PublicApi.Bills;

public sealed record BillPage(int Release, BillRow[] Bills, string? NextCursor)
{
    /// <summary>The cursor of the previous page of the list page. The JSON route reads no previous page cursor, so its answer does not carry one.</summary>
    [JsonIgnore] public string? PreviousCursor { get; init; }
}
