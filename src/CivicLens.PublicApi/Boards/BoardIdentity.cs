using CivicLens.Publication.Contracts;

namespace CivicLens.PublicApi.Boards;

public sealed record BoardIdentity(PublishedChamber Chamber, string Party);
