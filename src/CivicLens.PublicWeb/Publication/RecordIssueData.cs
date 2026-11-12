namespace CivicLens.PublicWeb.Publication;

/// <summary>An issue that a record page names. A configured issue has the name that the active release holds and a page to link to. An unconfigured issue shows its ID as plain text, as the static page does.</summary>
public sealed record RecordIssueData(string IssueId, string Name, bool IsConfigured);
