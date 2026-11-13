namespace CivicLens.PublicWeb.Publication;

/// <summary>One page of the weekly briefs list from one active release. <c>Total</c> counts every published brief, not only this page.</summary>
public sealed record BriefsPageData(int Release, DateTimeOffset PublishedAtUtc, int Page, int Total, BriefListItemData[] Briefs);
