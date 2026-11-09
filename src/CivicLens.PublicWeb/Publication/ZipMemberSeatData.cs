using CivicLens.Publication.Contracts;

namespace CivicLens.PublicWeb.Publication;

public sealed record ZipMemberSeatData(string Title, ZipMemberIdentityData? Member)
{
    public PublishedChamber Chamber { get; init; } = PublishedChamber.House;
}
