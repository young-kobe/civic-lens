using CivicLens.Publication.Contracts;

namespace CivicLens.PublicWeb.Publication;

/// <summary>One document type with its count and its newest actions.</summary>
public sealed record OfficialExecutiveType(PublishedExecutiveActionKind Kind, int Count, OfficialExecutiveRow[] Newest);
