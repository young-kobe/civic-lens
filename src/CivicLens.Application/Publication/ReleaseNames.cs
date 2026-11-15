using System.Collections.Immutable;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

/// <summary>The officials, issues, and policy areas that a release shows. Publish stores them with the release number.</summary>
/// <param name="SortNames">The roster last name of each official that a roster names, by official ID. <see cref="PublicationOfficials"/> owns the rule.</param>
public sealed record ReleaseNames(ImmutableArray<PublishedOfficialProfile> Officials, ReleaseFacets Facets, IReadOnlyDictionary<string, string> SortNames);
