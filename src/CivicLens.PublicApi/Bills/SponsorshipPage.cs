namespace CivicLens.PublicApi.Bills;

public sealed record SponsorshipPage(int Release, SponsorshipRow[] Sponsorships, string? NextCursor);
