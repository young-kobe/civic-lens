using CivicLens.PublicApi.Officials;
using CivicLens.Publication.Contracts;

namespace CivicLens.PublicApi.Maps;

public sealed record MapMemberAnswer(string OfficialId, string Name, PublishedOfficeTerm[] Terms, string? Party, DateOnly? TermEndsOn,
    MissedVotesFigure? Attendance, MapIssueActivityAnswer? IssueActivity, string PagePath);
