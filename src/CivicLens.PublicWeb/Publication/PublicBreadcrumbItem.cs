namespace CivicLens.PublicWeb.Publication;

/// <summary>One link of a breadcrumb trail. Every link leads to a page.</summary>
public sealed record PublicBreadcrumbItem(string Text, string PagePath);
