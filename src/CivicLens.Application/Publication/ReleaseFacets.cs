using System.Collections.Immutable;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

public sealed record ReleaseFacets(ImmutableArray<ReleaseFacet> Issues, ImmutableArray<ReleaseFacet> PolicyAreas)
{
    public static ReleaseFacets From(PublicationRelease release, PublicationCatalog catalog) => new(
        Group(release, entry => entry.IssueIds, catalog.IssueName, catalog.DefinitionOf),
        Group(release, entry => entry.PolicyAreaIds, catalog.PolicyAreaName, _ => null));

    private static ImmutableArray<ReleaseFacet> Group(PublicationRelease release, Func<PublishedRecordEntry, string[]> idsOf,
        Func<string, string> nameOf, Func<string, IssueDefinition?> definitionOf) =>
        [.. release.Records.SelectMany(idsOf).Distinct(StringComparer.Ordinal)
            .Select(id => new ReleaseFacet(id, nameOf(id), definitionOf(id)))
            .OrderBy(facet => facet.Name, StringComparer.Ordinal).ThenBy(facet => facet.Id, StringComparer.Ordinal)];
}
