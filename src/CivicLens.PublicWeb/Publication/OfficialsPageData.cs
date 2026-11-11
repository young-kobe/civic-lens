using CivicLens.Publication.Contracts;

namespace CivicLens.PublicWeb.Publication;

/// <summary>One page of the officials list. <see cref="RecordCountScope"/> says what each row's record count counts.
/// <see cref="NextCursor"/> is the opaque cursor of the next page, or null on the last page.</summary>
public sealed record OfficialsPageData(int Release, string RecordCountScope, IReadOnlyList<OfficialListRowData> Officials,
    OfficialsFilterData Filter, string? NextCursor);

public sealed record OfficialListRowData(string OfficialId, string Name, PublishedOfficeKind? Kind, string? Office, string? State,
    int? District, string? Party, string? Caucus, DateOnly? TermEndsOn, int RecordCount);

/// <summary>The filters of one request, as the reader typed them. A null value is no filter.</summary>
public sealed record OfficialsFilterData(string? Chamber, string? State, string? Party, string? Name)
{
    public static readonly OfficialsFilterData None = new(null, null, null, null);

    public bool IsActive => Chamber is not null || State is not null || Party is not null || Name is not null;
}
