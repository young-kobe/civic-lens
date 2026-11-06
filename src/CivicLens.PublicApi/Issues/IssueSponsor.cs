namespace CivicLens.PublicApi.Issues;

/// <summary>An official and the count of bill records on one issue that the official sponsored in the current Congress. <c>Base</c> is the count of every bill record on the issue, in any Congress, the number that <c>SponsoredBills</c> is part of.</summary>
public sealed record IssueSponsor(string OfficialId, int SponsoredBills, int Base);
