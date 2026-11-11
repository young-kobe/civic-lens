using CivicLens.PublicWeb.Publication;

namespace CivicLens.PublicApi.Bills;

/// <summary>The configured names of issues of the active release, in name order.</summary>
public sealed record BillIssueNames(int Release, BillIssueData[] Issues);
