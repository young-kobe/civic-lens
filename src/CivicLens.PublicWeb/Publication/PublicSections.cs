using CivicLens.Publication.Contracts;

namespace CivicLens.PublicWeb.Publication;

public static class PublicSections
{
    /// <summary>One link of the header or footer. HiddenText is read by assistive technology after the link text.</summary>
    public sealed record NavLink(PublicSection? Section, string Text, string Path, string? HiddenText = null);

    /// <summary>The path of the party boards page. The cutover PR maps it to the server page.</summary>
    public const string BoardsPath = "boards.html";

    public static readonly IReadOnlyList<NavLink> Header =
    [
        new(PublicSection.Officials, "Officials", PublicationProtocol.OfficialsPagePath),
        new(PublicSection.Issues, "Issues", PublicationProtocol.FacetsPath),
        new(PublicSection.Votes, "Votes", PublicationProtocol.SearchByKindPath(PublishedRecordKind.Vote)),
        new(PublicSection.Bills, "Bills", PublicationProtocol.SearchByKindPath(PublishedRecordKind.Action)),
        new(PublicSection.ExecutiveActions, "Executive actions", PublicationProtocol.SearchByKindPath(PublishedRecordKind.ExecutiveAction)),
        new(PublicSection.Statements, "Statements", PublicationProtocol.SearchByKindPath(PublishedRecordKind.Statement)),
        new(PublicSection.Boards, "Compare parties", BoardsPath)
    ];

    /// <summary>The old all records list. It is outside the header, and it names its own page in the trail.</summary>
    private static readonly NavLink AllRecords = new(PublicSection.AllRecords, "Votes and bills", PublicationProtocol.AllRecordsPagePath(1));

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
    /// The breadcrumb trail of an inner page. A record page runs Home, the section of its kind, then its short name. Another page runs
    /// Home, its section, then its title. The section link is absent when it leads to the page itself.
    /// </summary>
    public static PublicTrail Trail(PublicSection? section, string title, string? canonicalPath, ReportedRecord? record)
    {
        var home = new PublicBreadcrumbItem("Home", PublicationProtocol.HomePath);
        if (record is not null) return new([home, RecordSection(record.Kind)], record.ShortName ?? title);

        var link = Header.Concat(Footer).Append(AllRecords).FirstOrDefault(candidate => section is not null && candidate.Section == section);
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
