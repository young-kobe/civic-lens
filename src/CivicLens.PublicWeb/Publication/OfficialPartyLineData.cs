using CivicLens.Publication.Contracts;

namespace CivicLens.PublicWeb.Publication;

/// <summary>The Yea or Nay casts of the official with and against the party majority on split votes of one chamber and Congress.</summary>
public sealed record OfficialPartyLineData(PublishedChamber Chamber, int Congress, string Party, int WithParty, int AgainstParty, int Cast,
    DateOnly FirstVotedOn, DateOnly LastVotedOn);
