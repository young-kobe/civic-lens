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
    /// <summary>The day of the release. A term that ended before it is a former term.</summary>
    public DateOnly? PublishedOn { get; init; }
    public OfficialAttendanceData[] Attendance { get; init; } = [];
    public OfficialPartyLineData[] PartyLines { get; init; } = [];
    public int? BillsCongress { get; init; }
    public int SponsoredBills { get; init; }
    /// <summary>True when the release has no stored bill count for the Congress of the page, so the bill figure is absent and never zero.</summary>
    public bool BillCountsAbsent { get; init; }
    public int? IssuesCongress { get; init; }
    public OfficialIssueData[] Issues { get; init; } = [];
    public OfficialMarkData[] Marks { get; init; } = [];
    /// <summary>How many marks the official has in all. <c>Marks</c> holds only the newest ones.</summary>
    public int MarksTotal { get; init; }
    public SharedVoteRowData[] Votes { get; init; } = [];
    public OfficialStatementData[] Statements { get; init; } = [];
    /// <summary>True when a reviewer linked any statement of the official to a vote or a bill. <c>Statements</c> lists only the vote links.</summary>
    public bool HasLinkedStatements { get; init; }
    public OfficialBillData[] Bills { get; init; } = [];
    /// <summary>How many bills the official sponsored in every Congress of the release. It is the count of the bills tab of the record page. <c>Bills</c> holds only the newest ones of one Congress.</summary>
    public int RecordBills { get; init; }
    public OfficialExecutiveData? Executive { get; init; }
}
