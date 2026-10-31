using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

/// <summary>One public board page: the officials of one party in one chamber.</summary>
public sealed record OfficialBoard(PublishedChamber Chamber, string Party)
{
    public static readonly IReadOnlyList<OfficialBoard> All =
    [.. Enum.GetValues<PublishedChamber>().SelectMany(chamber =>
        new[] { PublishedPartyMajority.Democrat, PublishedPartyMajority.Republican }.Select(party => new OfficialBoard(chamber, party)))];

    public string PagePath => PublicationProtocol.BoardPagePath(Chamber, Party);
}
