using CivicLens.Publication.Contracts;

namespace CivicLens.PublicApi.ExecutiveActions;

/// <summary>The cited evidence of one executive action revision. <c>Facts</c> keeps the order of <see cref="PublishedExecutiveAction.Facts"/>, and each
/// fact cites one of <c>Sources</c>. <c>Quotes</c> holds the quote of each summary sentence in position order. They all cite <c>QuoteSource</c>, the body
/// text. Both are empty or null when the revision has no AI summary.</summary>
public sealed record ExecutiveActionEvidence(PublishedExecutiveActionFact[] Facts, PublishedDocumentVersion[] Sources, PublishedCitation[] Quotes,
    PublishedDocumentVersion? QuoteSource);
