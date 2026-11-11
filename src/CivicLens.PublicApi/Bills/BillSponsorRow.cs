using CivicLens.Publication.Contracts;

namespace CivicLens.PublicApi.Bills;

/// <summary>One sponsorship of a bill with the name and party of the official. The party is the one that the release names on its publication day.</summary>
public sealed record BillSponsorRow(
    int Position, string OfficialId, string Name, string? Party, PublishedSponsorshipRole Role, bool IsOriginal, DateOnly SponsoredOn, DateOnly? WithdrawnOn);
