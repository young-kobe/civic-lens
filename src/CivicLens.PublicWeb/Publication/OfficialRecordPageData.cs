using CivicLens.Publication.Contracts;

namespace CivicLens.PublicWeb.Publication;

/// <summary>One tab of the record page: a kind and how many records the official has of it in the release.</summary>
public sealed record OfficialRecordCountData(OfficialRecordKind Kind, int Count);

/// <summary>
/// Everything the server-rendered record page of one official shows, read from one release. <c>Counts</c> lists only the kinds with records, so a kind with none has no tab.
/// <c>Selected</c> is the kind of the page, and only its list is filled. <c>NextCursor</c> is null on the last page.
/// </summary>
public sealed record OfficialRecordPageData(int Release, string OfficialId, string Name, PublishedOfficeKind? Kind, OfficialRecordCountData[] Counts, OfficialRecordKind Selected)
{
    public SharedVoteRowData[] Votes { get; init; } = [];
    public OfficialBillData[] Bills { get; init; } = [];
    public OfficialRecordStatementData[] Statements { get; init; } = [];
    public OfficialRecordExecutiveData[] ExecutiveActions { get; init; } = [];
    public OfficialOfficeData[] Offices { get; init; } = [];
    public string? NextCursor { get; init; }

    public int SelectedCount => Counts.FirstOrDefault(count => count.Kind == Selected)?.Count ?? 0;
}
