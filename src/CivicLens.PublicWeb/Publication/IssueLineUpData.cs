namespace CivicLens.PublicWeb.Publication;

/// <summary>The bills of one issue by sponsor party in one Congress. <c>Parties</c> holds each party and its bill count, most first. The parties and <c>UnknownBills</c> add up to <c>Bills</c>.</summary>
public sealed record IssueLineUpData(int Congress, int Bills, IssueLineUpPartyData[] Parties, int UnknownBills);

public sealed record IssueLineUpPartyData(string Party, int Bills);
