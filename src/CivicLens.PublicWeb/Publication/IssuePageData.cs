namespace CivicLens.PublicWeb.Publication;

/// <summary>The inputs of the issue page. <c>Definition</c> is the written definition of the issue, or null. <c>SubstantiveVotes</c> and <c>BillCount</c> count the whole release. <c>Votes</c> and <c>Bills</c> hold only the newest rows. <c>LineUp</c> is null when the release holds no line-up.</summary>
public sealed record IssuePageData(int Release, string IssueId, string Name, string? Definition, int SubstantiveVotes, int BillCount, IssueVoteData[] Votes, IssueBillData[] Bills, IssueLineUpData? LineUp);
