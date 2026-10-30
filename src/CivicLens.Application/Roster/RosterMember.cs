namespace CivicLens.Application.Roster;

public sealed record RosterMember(string BioguideId, string FullName, string? Office, string? Phone,
    string? Url, RosterTerm[] Terms);
