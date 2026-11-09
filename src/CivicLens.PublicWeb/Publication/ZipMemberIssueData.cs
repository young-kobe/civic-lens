namespace CivicLens.PublicWeb.Publication;

public sealed record ZipMemberIssueData(string Name, int Congress, int SponsoredBills, int VotesCast, string Scope, string? PagePath = null);
