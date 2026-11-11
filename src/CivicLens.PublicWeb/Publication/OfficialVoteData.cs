using CivicLens.Publication.Contracts;

namespace CivicLens.PublicWeb.Publication;

/// <summary>One recent substantive vote with the cast of the official, in the plain words of <see cref="PlainVoteWords"/>.</summary>
public sealed record OfficialVoteData(string RecordId, DateOnly VotedOn, PublishedChamber Chamber, int RollCall, string Headline, string ResultWords,
    string Cast, string CastWords);
