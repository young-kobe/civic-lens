namespace CivicLens.PublicWeb.Publication;

public sealed record ZipMemberIdentityData(string OfficialId, string Name, string? Party, DateOnly? TermEndsOn, string? PhotoPath,
    ZipMemberAttendanceData[] Attendance, ZipMemberPartyLineData[] PartyLines)
{
    public ZipMemberBillsData? Bills { get; init; }
    public ZipMemberIssueData[] Issues { get; init; } = [];
}
