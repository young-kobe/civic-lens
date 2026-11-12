namespace CivicLens.PublicApi.Bills;

/// <summary>The sponsorships of a bill in the active release, in position order.</summary>
public sealed record BillSponsors(int Release, BillSponsorRow[] Rows);
