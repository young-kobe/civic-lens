using CivicLens.Publication.Contracts;

namespace CivicLens.PublicApi.Maps;

public sealed record MapAnswer(int Release, int? Congress, PublishedChamber Chamber, MapLayer Layer, DateOnly? AsOf,
    MapIssueAnswer? Issue, MapMemberAnswer[] Members, MapSeatAnswer[] Seats);
