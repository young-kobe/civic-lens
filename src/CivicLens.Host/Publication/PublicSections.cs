using CivicLens.Publication.Contracts;

namespace CivicLens.Host.Publication;

internal static class PublicSections
{
    public sealed record Link(PublicSection Section, string Label, string Path);

    public static readonly IReadOnlyList<Link> All =
    [
        new(PublicSection.Officials, "Officials", PublicationProtocol.OfficialsPagePath),
        new(PublicSection.Issues, "Issues", PublicationProtocol.FacetsPath),
        new(PublicSection.Briefs, "Briefs", PublicationProtocol.BriefsPagePath(1)),
        new(PublicSection.AllRecords, "All records", PublicationProtocol.AllRecordsPagePath(1)),
        new(PublicSection.Search, "Search", PublicationProtocol.SearchPagePath)
    ];

    public static Link Of(PublicSection section) => All.Single(link => link.Section == section);
}
