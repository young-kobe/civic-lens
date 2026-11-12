namespace CivicLens.PublicWeb.Publication;

/// <summary>A breadcrumb trail. The parents are links. The current page is text.</summary>
public sealed record PublicTrail(IReadOnlyList<PublicBreadcrumbItem> Parents, string Current);
