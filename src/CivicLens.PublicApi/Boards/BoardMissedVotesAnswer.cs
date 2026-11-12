namespace CivicLens.PublicApi.Boards;

public sealed record BoardMissedVotesAnswer(int Missed, int Held, DateOnly FirstVotedOn, DateOnly LastVotedOn, string Scope);
