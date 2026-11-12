namespace CivicLens.PublicWeb.Publication;

/// <summary>One correction notice of an executive action, in position order.</summary>
public sealed record ExecutiveActionNoticeData(string DocumentNumber, DateOnly PublishedOn, string Url);
