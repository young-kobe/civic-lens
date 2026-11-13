namespace CivicLens.PublicWeb.Publication;

/// <summary>An issue that a record page names, with the name that the active release holds and a page to link to. A record page leaves out an issue that the release does not configure, because its ID means nothing to a reader.</summary>
public sealed record RecordIssueData(string IssueId, string Name);
