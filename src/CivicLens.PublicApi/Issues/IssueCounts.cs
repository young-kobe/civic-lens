namespace CivicLens.PublicApi.Issues;

/// <summary>The counts of one tracked issue in the active release. <c>SubstantiveVotes</c> counts the vote records that hold the issue and have a substantive kind. <c>Bills</c> counts the bill records that hold the issue.
/// <c>TopSponsors</c> lists the officials with the most sponsored bills on the issue in the current Congress, most first, then by official ID.</summary>
public sealed record IssueCounts(int Release, string IssueId, int SubstantiveVotes, int Bills, IssueSponsor[] TopSponsors);
