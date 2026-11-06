namespace CivicLens.PublicApi.Officials;

/// <summary>The bills that the official sponsored and the substantive votes that the official cast on one tracked issue, in one Congress.</summary>
public sealed record IssueCountsFigure(string IssueId, int SponsoredBills, int VotesCast);
