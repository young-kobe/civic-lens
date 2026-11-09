using CivicLens.PublicApi.Officials;

namespace CivicLens.PublicApi.ZipDistricts;

/// <summary>The districts of one ZCTA with the people who hold their seats in the active release. The districts follow the largest land share first.
/// The states follow the order of their first district.</summary>
public sealed record ZipDistrictAnswer(int Release, int Congress, string Zcta, ZipDistrict[] Districts, ZipState[] States);

/// <summary>One House seat. A null representative means that the seat is vacant. District 0 is an at-large seat or a delegate seat.</summary>
public sealed record ZipDistrict(string State, int District, SeatHolder? Representative);

/// <summary>The Senate seats of one state or territory. A null entry is a vacant seat. A territory has no entry.</summary>
public sealed record ZipState(string State, SeatHolder?[] Senators);

/// <summary>The person who holds a seat. <c>TermEndsOn</c> is the end date of the current term, or null when the release has no end date.</summary>
public sealed record SeatHolder(string OfficialId, string Name, string? Party, DateOnly? TermEndsOn, string? PhotoPath, MissedVotesFigure[] MissedVotes, PartyLineFigure[] PartyLines)
{
    public BillCountsFigure? Bills { get; init; }
    public IssueCountsFigures? Issues { get; init; }
}
