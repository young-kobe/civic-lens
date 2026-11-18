namespace CivicLens.PublicWeb.Publication;

public sealed record MapMemberData(string OfficialId, string Name, string PagePath, string Seat, string? Party, DateOnly? TermEndsOn,
    MapFraction? Attendance, MapFraction? Bills, MapFraction? Votes, bool HasSeat);
