namespace CivicLens.Application.Roster;

/// <param name="LastName">The roster <c>name.last</c>. Publication stores it as the sort name of the official.</param>
public sealed record ExecutiveOfficial(int GovtrackId, string? WikidataId, string FullName, string? LastName, ExecutiveTerm[] Terms);
