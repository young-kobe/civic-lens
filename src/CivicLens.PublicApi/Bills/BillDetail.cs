namespace CivicLens.PublicApi.Bills;

public sealed record BillDetail(int Release, BillRow Bill, string[] IssueIds);
