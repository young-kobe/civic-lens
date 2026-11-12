namespace CivicLens.PublicWeb.Publication;

/// <summary>The tracked issues of one active release, by name. An issue the release has not counted yet has no counts.</summary>
public sealed record IssuesIndexData(int Release, IssuesIndexItemData[] Issues);
