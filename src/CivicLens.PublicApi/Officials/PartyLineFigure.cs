using CivicLens.Publication.Contracts;

namespace CivicLens.PublicApi.Officials;

/// <summary>The official's Yea or Nay casts with and against the pinned party majority on substantive split votes.</summary>
public sealed record PartyLineFigure(PublishedChamber Chamber, int Congress, string Party, int WithParty, int AgainstParty, int Cast,
    DateOnly FirstVotedOn, DateOnly LastVotedOn, string Scope);
