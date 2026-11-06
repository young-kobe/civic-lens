namespace CivicLens.PublicApi.Issues;

/// <summary>A tracked official and the count of bill records on one issue that the official sponsored. <c>Base</c> is the count of every bill record on the issue, the number that <c>SponsoredBills</c> is part of.</summary>
public sealed record IssueSponsor(string OfficialId, int SponsoredBills, int Base);
