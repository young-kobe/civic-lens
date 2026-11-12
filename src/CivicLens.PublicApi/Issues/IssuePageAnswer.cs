namespace CivicLens.PublicApi.Issues;

/// <summary>What the issue page reads from one release snapshot. <c>Definition</c> is the written definition of the issue, or null. <c>Includes</c> and <c>Excludes</c> are the terms that the release stores for the issue, in stored order.
/// <c>Congress</c> is the Congress of the release day, or null when the release stores none. A null Congress does not narrow the counts or the lists.
/// <c>SubstantiveVotes</c> counts the substantive roll calls on the issue in that Congress and <c>SplitVotes</c> counts those whose stored alignment is split. <c>BillCount</c> counts the bills of the issue in that Congress.
/// <c>Votes</c> holds the newest of those roll calls and <c>Bills</c> the newest of those bills, most recent first.
/// <c>LineUp</c> is null for a release that has no line-up.</summary>
public sealed record IssuePageAnswer(int Release, string IssueId, string Name, string? Definition, string[] Includes, string[] Excludes, int? Congress, int SubstantiveVotes, int SplitVotes, int BillCount, IssuePartyLineUp? LineUp, IssueVote[] Votes, IssueBill[] Bills);
