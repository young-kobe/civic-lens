namespace CivicLens.PublicApi.Issues;

/// <summary>The counts of one tracked issue in the active release. <c>Votes</c> counts every vote record that holds the issue. <c>SubstantiveVotes</c> counts those of a substantive kind.
/// <c>Bills</c> counts the bill records that hold the issue. <c>TopSponsors</c> lists the tracked officials with the most sponsored bills on the issue, most first, then by official ID.</summary>
public sealed record IssueCounts(int Release, string IssueId, int Votes, int SubstantiveVotes, int Bills, IssueSponsor[] TopSponsors);
