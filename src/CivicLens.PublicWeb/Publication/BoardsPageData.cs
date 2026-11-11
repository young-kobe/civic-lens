using CivicLens.Publication.Contracts;

namespace CivicLens.PublicWeb.Publication;

/// <summary>The four party boards of the active release and the party agreement of each chamber.</summary>
public sealed record BoardsPageData(int Release, IReadOnlyList<BoardPanelData> Boards, IReadOnlyList<BoardAgreementData> Agreements);

/// <summary>One chamber and party. A null Congress means that the release holds no vote figures for the board.
/// <see cref="Rows"/> hold the members who voted against most of their party, highest share first.</summary>
public sealed record BoardPanelData(PublishedChamber Chamber, string Party, int? Congress, IReadOnlyList<BoardRowData> Rows,
    string? PartyLineScope);

/// <summary><see cref="Seat"/> names a delegate or resident commissioner, and is null for a voting member.</summary>
public sealed record BoardRowData(string OfficialId, string Name, string? Seat, int Cast, int Breaks);

public sealed record BoardAgreementData(PublishedChamber Chamber, int Congress, IReadOnlyList<BoardAgreementKindData> Kinds,
    int Agreed, int Split, int Mixed, int Total, int Nominations);

public sealed record BoardAgreementKindData(PublishedVoteKind Kind, int Agreed, int Total);
