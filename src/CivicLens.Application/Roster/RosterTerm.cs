namespace CivicLens.Application.Roster;

public sealed record RosterTerm(int Index, string Chamber, DateOnly Start, DateOnly End, string State, int? District,
    int? SenateClass, string Party);
