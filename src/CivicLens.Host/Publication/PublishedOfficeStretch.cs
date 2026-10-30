using CivicLens.Publication.Contracts;

namespace CivicLens.Host.Publication;

public sealed record PublishedOfficeStretch(PublishedOfficeTerm Latest, DateOnly Start, DateOnly? End, int Terms);
