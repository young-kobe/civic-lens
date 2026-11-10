using CivicLens.Publication.Contracts;

namespace CivicLens.PublicWeb.Publication;

public static class PublicSections
{
    public sealed record NavLink(PublicSection? Section, string Text, string Path);

    public static readonly IReadOnlyList<NavLink> Header =
    [
        new(PublicSection.Officials, "Officials", PublicationProtocol.OfficialsPagePath),
        new(PublicSection.Issues, "Issues", PublicationProtocol.FacetsPath),
        new(PublicSection.AllRecords, "Votes and bills", PublicationProtocol.AllRecordsPagePath(1))
    ];

    public static readonly NavLink Report = new(null, "Report a problem", PublicationProtocol.ReportPagePath);

    public static readonly IReadOnlyList<NavLink> Footer =
    [
        new(null, "How we check records", PublicationProtocol.MethodPagePath),
        new(PublicSection.Glossary, "Glossary", PublicationProtocol.GlossaryPagePath),
        new(PublicSection.About, "About", PublicationProtocol.AboutPagePath),
        Report
    ];

    /// <summary>The breadcrumb trail of an inner page: Home, the section when it differs from the page, then the page.</summary>
    public static IReadOnlyList<PublicBreadcrumbItem> Trail(PublicSection? section, string title)
    {
        var trail = new List<PublicBreadcrumbItem> { new("Home", PublicationProtocol.HomePath) };
        var link = Header.Concat(Footer).FirstOrDefault(candidate => section is not null && candidate.Section == section);
        if (link is not null && link.Text != title) trail.Add(new(link.Text, link.Path));
        trail.Add(new(title, null));
        return trail;
    }
}
