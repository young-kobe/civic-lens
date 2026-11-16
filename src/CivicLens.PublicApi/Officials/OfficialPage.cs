using System.Text.Json.Serialization;

namespace CivicLens.PublicApi.Officials;

/// <summary>A page of officials. <c>RecordCountScope</c> says what each <c>recordCount</c> counts. <c>OfficialsInRelease</c> counts every official of the active release, and <c>OfficialsInReleaseScope</c> says so.</summary>
public sealed record OfficialPage(int Release, string RecordCountScope, int OfficialsInRelease, string OfficialsInReleaseScope, OfficialRow[] Officials, string? NextCursor)
{
    /// <summary>The cursor of the previous page of the list page. The JSON route reads no previous page cursor, so its answer does not carry one.</summary>
    [JsonIgnore] public string? PreviousCursor { get; init; }
}
