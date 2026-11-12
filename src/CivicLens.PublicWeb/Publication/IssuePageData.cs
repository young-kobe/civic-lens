namespace CivicLens.PublicWeb.Publication;

/// <summary>The inputs of the issue page. <c>Definition</c> is the written definition of the issue, or null.
/// <c>Congress</c> is the Congress of the release day, or null when the release stores none. The counts and the lists cover that Congress.
/// <c>SubstantiveVotes</c>, <c>SplitVotes</c>, and <c>BillCount</c> count all rows of the issue in it. <c>Votes</c> and <c>Bills</c> hold only the newest rows. <c>LineUp</c> is null when the release holds no line-up.</summary>
public sealed record IssuePageData(int Release, string IssueId, string Name, string? Definition, int? Congress, int SubstantiveVotes, int SplitVotes, int BillCount, IssueVoteData[] Votes, IssueBillData[] Bills, IssueLineUpData? LineUp);
