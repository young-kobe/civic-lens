using CivicLens.Publication.Contracts;

namespace CivicLens.PublicWeb.Publication;

/// <summary>The AI summary of an executive action as the public view serves it. The label is null when the origin is not Model. <c>Cut</c> is true when the model read only the first window of the document.</summary>
public sealed record ExecutiveActionSummaryData(PublishedTextOrigin TextOrigin, string? Label, bool Cut, string[] Sentences);
