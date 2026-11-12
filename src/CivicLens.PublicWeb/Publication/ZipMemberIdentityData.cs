namespace CivicLens.PublicWeb.Publication;

public sealed record ZipMemberIdentityData(string OfficialId, string Name, string? Party, DateOnly? TermEndsOn, string? PhotoPath,
    ZipMemberAttendanceData[] Attendance, ZipMemberPartyLineData[] PartyLines)
{
    public ZipMemberBillsData? Bills { get; init; }
    /// <summary>True when the release has no stored bill count for the Congress of the card, so the bill figure is absent and never zero.</summary>
    public bool BillCountsAbsent { get; init; }
    public ZipMemberIssueData[] Issues { get; init; } = [];
}
