using CivicLens.Publication.Contracts;

namespace CivicLens.Host.Publication;

internal sealed record PublishedOfficeStretch(PublishedOfficeTerm Latest, DateOnly Start, DateOnly? End);
