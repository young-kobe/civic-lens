using CivicLens.Publication.Contracts;

namespace CivicLens.PublicWeb.Publication;

/// <summary>One page of the officials list. <see cref="OfficialsInRelease"/> counts every official of the release. A filter does not change it.
/// <see cref="NextCursor"/> is the opaque cursor of the next page, or null on the last page.</summary>
public sealed record OfficialsPageData(int Release, int OfficialsInRelease, IReadOnlyList<OfficialListRowData> Officials,
    RecordListFilter Filter, string? NextCursor)
{
    /// <summary>The cursor of the previous page, or null on the first page.</summary>
    public string? PreviousCursor { get; init; }
}

public sealed record OfficialListRowData(string OfficialId, string Name, PublishedOfficeKind? Kind, string? Office, string? State,
    int? District, string? Party, DateOnly? TermEndsOn, int RecordCount);
