using CivicLens.Publication.Contracts;

namespace CivicLens.PublicWeb.Publication;

/// <summary>
/// Everything the server-rendered official page shows, read from one release. Figures and lists are absent or empty when the release holds no data for them.
/// <c>Executive</c> is set only for an official with executive actions, and that official gets the President layout.
/// </summary>
public sealed record OfficialPageData(
    int Release, string OfficialId, string Name, PublishedOfficeKind? Kind, string? Office, string? State, int? District, string? Party,
    string? Caucus, DateOnly? TermEndsOn, string? PhotoPath)
{
    public OfficialAttendanceData[] Attendance { get; init; } = [];
    public OfficialPartyLineData[] PartyLines { get; init; } = [];
    public int? BillsCongress { get; init; }
    public int SponsoredBills { get; init; }
    public OfficialIssueData[] Issues { get; init; } = [];
    public OfficialMarkData[] Marks { get; init; } = [];
    public OfficialVoteData[] Votes { get; init; } = [];
    public OfficialStatementData[] Statements { get; init; } = [];
    public OfficialBillData[] Bills { get; init; } = [];
    public OfficialExecutiveData? Executive { get; init; }
}
