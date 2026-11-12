using CivicLens.Publication.Contracts;

namespace CivicLens.PublicWeb.Publication;

/// <summary>The roll calls of one chamber and Congress that list the official. <c>Held</c> is the base. Missed means the cast is Not Voting.</summary>
public sealed record OfficialAttendanceData(PublishedChamber Chamber, int Congress, int Missed, int Held, DateOnly FirstVotedOn, DateOnly LastVotedOn);
