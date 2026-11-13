namespace CivicLens.PublicWeb.Publication;

/// <summary>One tracked issue. The counts are those of its Congress. All three are null together when the release has not counted the issue.</summary>
public sealed record IssuesIndexItemData(string IssueId, string Name, int? Congress, int? SubstantiveVotes, int? Bills);
