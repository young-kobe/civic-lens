namespace CivicLens.PublicWeb.Publication;

/// <summary>An official that the release tracks and that backed the bill. The party is the one the release names for the official on its publication day.</summary>
public sealed record BillSponsorData(string OfficialId, string Name, string? Party, DateOnly SponsoredOn);
