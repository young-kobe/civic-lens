using CivicLens.PublicWeb.Publication;

namespace CivicLens.PublicApi.Relationships;

/// <summary>One active relationship as <c>public_api.relationships</c> gives it. The page names the speakers and issues from the IDs of its published entry.</summary>
public sealed record RelationshipDetail(RelationshipPageData Page, string[] OfficialIds, string[] IssueIds);
