using CivicLens.Publication.Contracts;

namespace CivicLens.PublicWeb.Publication;

/// <summary>The four party boards of the active release and the party agreement of each chamber.</summary>
public sealed record BoardsPageData(int Release, IReadOnlyList<BoardPanelData> Boards, IReadOnlyList<BoardAgreementData> Agreements);

/// <summary>One chamber and party. A null Congress means that the release holds no vote figures for the board.
/// <see cref="NewerCongress"/> is the newest Congress of the chamber while the board keeps an older one, else null.
/// <see cref="NewestRollCall"/> is the day of the newest roll call that the counts include.
/// <see cref="Rows"/> hold the members who voted against most of their party at least once, highest share first.
/// <see cref="Ranked"/> says whether any member had enough split votes to be ranked, so an empty list is told apart from no ranked member.</summary>
public sealed record BoardPanelData(PublishedChamber Chamber, string Party, int? Congress, int? NewerCongress, DateOnly? NewestRollCall,
    bool Ranked, IReadOnlyList<BoardRowData> Rows);

/// <summary><see cref="Seat"/> names a delegate or resident commissioner, and is null for a voting member.</summary>
/// <summary>One ranked member. <see cref="PagePath"/> is the official page of the Congress of the board, so the page figures match the count.</summary>
public sealed record BoardRowData(string PagePath, string Name, string? Seat, int Cast, int Breaks);

public sealed record BoardAgreementData(PublishedChamber Chamber, int Congress, IReadOnlyList<BoardAgreementKindData> Kinds,
    int Agreed, int Split, int Mixed, int Total, int Nominations);

public sealed record BoardAgreementKindData(PublishedVoteKind Kind, int Agreed, int Total);

/// <summary>The full list of one board of the active release.</summary>
public sealed record BoardListPageData(int Release, BoardPanelData Board);
