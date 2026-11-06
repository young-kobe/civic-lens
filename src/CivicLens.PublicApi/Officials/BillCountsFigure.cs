namespace CivicLens.PublicApi.Officials;

/// <summary>
/// The bills of one Congress that name the official. <c>BillsInCongress</c> is the base: every bill of that Congress in the active release.
/// A bill counts in the Congress that its bill number belongs to. A cosponsor counts when the cosponsorship is not withdrawn and the official is not also a sponsor of the bill.
/// </summary>
public sealed record BillCountsFigure(int Congress, int Sponsored, int Cosponsored, int BillsInCongress, string Scope);
