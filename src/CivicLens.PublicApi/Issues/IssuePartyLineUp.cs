namespace CivicLens.PublicApi.Issues;

/// <summary>How the bills of one issue split by the party of the sponsor. It counts bill records, not votes and not cosponsors, in one Congress.
/// <c>Congress</c> is the Congress of the release day. <c>Bills</c> is the bill count of the Home issue tile for that Congress.
/// <c>Parties</c> holds the bills whose sponsor held that party on the sponsorship date, most first. <c>UnknownBills</c> holds the bills with no sponsor party that one term fixes.
/// The parties and <c>UnknownBills</c> add up to <c>Bills</c>.</summary>
public sealed record IssuePartyLineUp(int Congress, int Bills, IssuePartyCount[] Parties, int UnknownBills);
