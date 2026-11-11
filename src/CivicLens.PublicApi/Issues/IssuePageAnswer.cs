namespace CivicLens.PublicApi.Issues;

/// <summary>What the issue page reads from one release snapshot. <c>SubstantiveVotes</c> and <c>Bills</c> are the counts of <see cref="IssueCounts"/>.
/// <c>Votes</c> holds the newest substantive roll calls on the issue and <c>Bills</c> the newest bills, most recent first.
/// <c>LineUp</c> is null for a release that has no line-up.</summary>
public sealed record IssuePageAnswer(int Release, string IssueId, string Name, int SubstantiveVotes, int BillCount, IssuePartyLineUp? LineUp, IssueVote[] Votes, IssueBill[] Bills);
