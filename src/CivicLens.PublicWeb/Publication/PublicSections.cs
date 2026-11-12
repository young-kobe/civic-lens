using CivicLens.Publication.Contracts;

namespace CivicLens.PublicWeb.Publication;

public static class PublicSections
{
    /// <summary>One link of the header or footer. HiddenText is read by assistive technology after the link text.</summary>
    public sealed record NavLink(PublicSection? Section, string Text, string Path, string? HiddenText = null);

    public static readonly IReadOnlyList<NavLink> Header =
    [
        new(PublicSection.Officials, "Officials", PublicationProtocol.OfficialsPagePath),
        new(PublicSection.Issues, "Issues", PublicationProtocol.FacetsPath),
        new(PublicSection.AllRecords, "Votes and bills", PublicationProtocol.AllRecordsPagePath(1))
    ];

    public static readonly NavLink Report = new(null, $"{PublishedRecordLabels.ReportLinkStart} page", PublicationProtocol.ReportPagePath);

    public static readonly IReadOnlyList<NavLink> Footer =
    [
        new(null, "How we check records", PublicationProtocol.MethodPagePath),
        new(PublicSection.Glossary, "Glossary", PublicationProtocol.GlossaryPagePath),
        new(PublicSection.About, "About", PublicationProtocol.AboutPagePath),
        Report
    ];

    /// <summary>The footer links of a page. A record page swaps the report link for the one that names its record.</summary>
    public static IReadOnlyList<NavLink> FooterFor(ReportedRecord? record) =>
        record is null ? Footer : [.. Footer.Select(link => link == Report ? record.ReportLink : link)];

    /// <summary>
    /// The breadcrumb trail of an inner page. A page of one kind runs Home, the section of its kind, then its short name. Another page runs
    /// Home, its section, then its title. The section link is absent when it leads to the page itself.
    /// </summary>
    public static PublicTrail Trail(PublicSection? section, string title, string? canonicalPath, PublishedRecordKind? kind, string? shortName)
    {
        var home = new PublicBreadcrumbItem("Home", PublicationProtocol.HomePath);
        if (kind is { } recordKind) return new([home, RecordSection(recordKind)], shortName ?? title);

        var link = Header.Concat(Footer).FirstOrDefault(candidate => section is not null && candidate.Section == section);
        if (link is null || link.Path == canonicalPath) return new([home], title);
        return new([home, new(link.Text, link.Path)], title);
    }

    private static PublicBreadcrumbItem RecordSection(PublishedRecordKind kind) => kind switch
    {
        PublishedRecordKind.Brief => new(PublishedRecordLabels.KindPlural(kind), PublicationProtocol.BriefsPagePath(1)),
        PublishedRecordKind.Action => new("Bills", PublicationProtocol.SearchByKindPath(kind)),
        _ => new(PublishedRecordLabels.KindPlural(kind), PublicationProtocol.SearchByKindPath(kind))
    };
}
