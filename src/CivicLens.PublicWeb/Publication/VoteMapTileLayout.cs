namespace CivicLens.PublicWeb.Publication;

/// <summary>The labels and the tiles of the tile view, in seat order, and the height they need.</summary>
public sealed record VoteMapTileLayout(IReadOnlyList<VoteMapTileLabel> Labels, IReadOnlyList<VoteMapTile> Tiles, double Height);
