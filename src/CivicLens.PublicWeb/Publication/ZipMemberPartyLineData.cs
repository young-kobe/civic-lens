using CivicLens.Publication.Contracts;

namespace CivicLens.PublicWeb.Publication;

public sealed record ZipMemberPartyLineData(PublishedChamber Chamber, int Congress, string Party, int WithParty, int Cast,
    DateOnly FirstVotedOn, DateOnly LastVotedOn, string Scope);
