using CivicLens.Publication.Contracts;

namespace CivicLens.PublicWeb.Publication;

public sealed record ZipMemberAttendanceData(PublishedChamber Chamber, int Congress, int Missed, int Held, DateOnly FirstVotedOn, DateOnly LastVotedOn, string Scope);
