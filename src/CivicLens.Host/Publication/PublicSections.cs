using CivicLens.Publication.Contracts;

namespace CivicLens.Host.Publication;

internal static class PublicSections
{
    public sealed record Link(PublicSection Section, string Label, string Path);

    public sealed record NavLink(PublicSection? Section, string Text, string Path);

    private static readonly IReadOnlyList<Link> All =
    [
        new(PublicSection.Officials, "Officials", PublicationProtocol.OfficialsPagePath),
        new(PublicSection.Issues, "Issues", PublicationProtocol.FacetsPath),
        new(PublicSection.Briefs, "Briefs", PublicationProtocol.BriefsPagePath(1)),
        new(PublicSection.AllRecords, "All records", PublicationProtocol.AllRecordsPagePath(1)),
        new(PublicSection.Search, "Search", PublicationProtocol.SearchPagePath),
        new(PublicSection.About, "About", PublicationProtocol.AboutPagePath),
        new(PublicSection.Glossary, "Glossary", PublicationProtocol.GlossaryPagePath)
    ];

    public static readonly IReadOnlyList<NavLink> Header =
    [
        Nav(PublicSection.Officials, "Officials"),
        Nav(PublicSection.Issues, "Issues"),
        Nav(PublicSection.AllRecords, "Votes and bills")
    ];

    public static readonly IReadOnlyList<NavLink> Footer =
    [
        Nav(PublicSection.Briefs, "Weekly briefs"),
        new(null, "How the counts work", PublicationProtocol.MethodPagePath),
        Nav(PublicSection.Glossary, "Glossary"),
        Nav(PublicSection.About, "About"),
        new(null, "Report a problem", PublicationProtocol.ReportPagePath)
    ];

    public static Link Of(PublicSection section) => All.Single(link => link.Section == section);

    private static NavLink Nav(PublicSection section, string text) => new(section, text, Of(section).Path);
}
