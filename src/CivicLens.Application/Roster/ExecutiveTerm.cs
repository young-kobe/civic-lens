namespace CivicLens.Application.Roster;

public sealed record ExecutiveTerm(int Index, string Office, DateOnly Start, DateOnly End, string Party);
