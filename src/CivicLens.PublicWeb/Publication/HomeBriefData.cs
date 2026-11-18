using CivicLens.Publication.Contracts;

namespace CivicLens.PublicWeb.Publication;

public sealed record HomeBriefData(string RecordId, string Headline, string? Label, PublishedTextOrigin TextOrigin);
