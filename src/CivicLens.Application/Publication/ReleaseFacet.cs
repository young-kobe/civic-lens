using System.Collections.Immutable;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

public enum FacetKind
{
    Issue,
    PolicyArea
}

/// <summary>The manifest entries that hold one issue or policy area ID, newest first.</summary>
public sealed record ReleaseFacet(FacetKind Kind, string Id, string Name, ImmutableArray<PublishedRecordEntry> Records)
{
    public bool HasPages => PublicationProtocol.IsPathSegment(Id);

    public int PageCount => PublicationProtocol.PageCount(Records.Length);

    public string PagePath(int page) => Kind == FacetKind.Issue
        ? PublicationProtocol.IssuePagePath(Id, page)
        : PublicationProtocol.PolicyAreaPagePath(Id, page);

    public IReadOnlyList<PublishedRecordEntry> RecordsOnPage(int page) =>
        [.. Records.Skip((page - 1) * PublicationProtocol.PageSize).Take(PublicationProtocol.PageSize)];
}
