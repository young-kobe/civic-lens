namespace CivicLens.PublicWeb.Publication;

/// <summary>An official that the release tracks and that backed the bill. The party is the one that the term of the official held on the sponsorship date. It is null when no single party held it.</summary>
public sealed record BillSponsorData(string OfficialId, string Name, string? Party, DateOnly SponsoredOn);
