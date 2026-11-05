using System.Collections.Immutable;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

/// <summary>The officials, issues, and policy areas that a release shows. Publish stores them with the release number.</summary>
public sealed record ReleaseNames(ImmutableArray<PublishedOfficialProfile> Officials, ReleaseFacets Facets);
