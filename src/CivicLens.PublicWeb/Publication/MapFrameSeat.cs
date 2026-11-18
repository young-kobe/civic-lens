namespace CivicLens.PublicWeb.Publication;

/// <summary>A seat's SVG coordinates, display tokens, and text. A seat without a path uses the specified half of its state when shapes are drawn.</summary>
/// <remarks>
/// The page may supply a validated internal path to make the seat a link.
/// A null tile omits the tile. DrawShape is false when the seat has no drawable geometry.
/// </remarks>
public sealed record MapFrameSeat(
    string State,
    string? Path,
    int Half,
    VoteMapTile? Tile,
    string Mark,
    string? Party,
    string Description,
    bool Emphasized = false,
    string? Href = null,
    bool DrawShape = true,
    double? FillOpacity = null);
