using System.Collections.Immutable;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

public sealed record ReleaseFacets(ImmutableArray<ReleaseFacet> Issues, ImmutableArray<ReleaseFacet> PolicyAreas)
{
    public IEnumerable<ReleaseFacet> All => Issues.Concat(PolicyAreas);

    public static ReleaseFacets From(PublicationRelease release, PublicationCatalog catalog) => new(
        Group(release, FacetKind.Issue, entry => entry.IssueIds, catalog.IssueName),
        Group(release, FacetKind.PolicyArea, entry => entry.PolicyAreaIds, catalog.PolicyAreaName));

    private static ImmutableArray<ReleaseFacet> Group(PublicationRelease release, FacetKind kind,
        Func<PublishedRecordEntry, string[]> idsOf, Func<string, string> nameOf) =>
        [.. release.Records.SelectMany(entry => idsOf(entry).Select(id => (Id: id, Entry: entry)))
            .GroupBy(pair => pair.Id, StringComparer.Ordinal)
            .Select(group => new ReleaseFacet(kind, group.Key, nameOf(group.Key), [.. group.Select(pair => pair.Entry)]))
            .OrderBy(facet => facet.Name, StringComparer.Ordinal).ThenBy(facet => facet.Id, StringComparer.Ordinal)];
}
