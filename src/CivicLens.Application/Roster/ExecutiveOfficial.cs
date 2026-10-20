namespace CivicLens.Application.Roster;

public sealed record ExecutiveOfficial(int GovtrackId, string? WikidataId, string FullName, ExecutiveTerm[] Terms);
