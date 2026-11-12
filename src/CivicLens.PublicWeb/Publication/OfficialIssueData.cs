namespace CivicLens.PublicWeb.Publication;

/// <summary>A tracked issue with the bills the official sponsored and the substantive votes the official cast on it, in one Congress.</summary>
public sealed record OfficialIssueData(string IssueId, string Name, int Records);
