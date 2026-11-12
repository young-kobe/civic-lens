namespace CivicLens.PublicWeb.Publication;

/// <summary>One row of the map table: a seat and its member, or no member when the record has no member row for the seat.</summary>
public sealed record VoteMapRow(string Seat, VoteMemberData? Member);
