using CivicLens.Publication.Contracts;

namespace CivicLens.PublicWeb.Publication;

public sealed record PublishedOfficeStretch(PublishedOfficeTerm Latest, DateOnly Start, DateOnly? End);
