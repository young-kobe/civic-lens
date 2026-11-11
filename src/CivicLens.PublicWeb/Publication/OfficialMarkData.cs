using CivicLens.Publication.Contracts;

namespace CivicLens.PublicWeb.Publication;

/// <summary>One mark of the vote strip: a split vote cast against most of the party, or a roll call the official missed. <c>Party</c> is the party that the vote broke from.</summary>
public sealed record OfficialMarkData(string RecordId, DateOnly VotedOn, PublishedChamber Chamber, int RollCall, bool IsMissed, string? Party);
