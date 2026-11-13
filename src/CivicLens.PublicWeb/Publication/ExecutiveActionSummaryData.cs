using CivicLens.Publication.Contracts;

namespace CivicLens.PublicWeb.Publication;

/// <summary>The AI summary of an executive action as the public view serves it. The label is null when the origin is not Model. <c>Cut</c> is true when the model read only the first window of the document.
/// Each sentence holds the exact quote of the body text that supports it. <c>Source</c> names that body text. The page links to it and never shows the whole text.</summary>
public sealed record ExecutiveActionSummaryData(PublishedTextOrigin TextOrigin, string? Label, bool Cut, PublishedExecutiveSentence[] Sentences, PublishedDocumentVersion? Source);
