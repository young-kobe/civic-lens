using CivicLens.Publication.Contracts;

namespace CivicLens.PublicWeb.Publication;

public static class PublicSections
{
    /// <summary>One link of the header or footer. HiddenText is read by assistive technology after the link text.</summary>
    public sealed record NavLink(PublicSection? Section, string Text, string Path, string? HiddenText = null);

    /// <summary>The path that the server boards page names as its own. The cutover PR maps it to that page.</summary>
    public const string BoardsPath = "boards.html";

    /// <summary>The static board that the header and the officials page link to, until the cutover PR maps <see cref="BoardsPath"/>.</summary>
    public static readonly string StaticBoardsPath = PublicationProtocol.BoardPagePath(PublishedChamber.House, PublishedPartyMajority.Democrat);

    public static readonly IReadOnlyList<NavLink> Header =
    [
        new(PublicSection.Officials, "Officials", PublicationProtocol.OfficialsPagePath),
        new(PublicSection.Issues, "Issues", PublicationProtocol.FacetsPath),
        new(PublicSection.Votes, "Votes", PublicationProtocol.SearchByKindPath(PublishedRecordKind.Vote)),
        new(PublicSection.Bills, "Bills", PublicationProtocol.SearchByKindPath(PublishedRecordKind.Action)),
        new(PublicSection.ExecutiveActions, "Executive actions", PublicationProtocol.SearchByKindPath(PublishedRecordKind.ExecutiveAction)),
        new(PublicSection.Boards, "Compare parties", StaticBoardsPath)
    ];

    /// <summary>The origin-relative path of the server issues index on the gated preview. It stays on the origin that serves the preview, because the public origin does not route it. Only a server page links it, until the cutover PR maps <see cref="PublicationProtocol.FacetsPath"/>.</summary>
    public const string PreviewIssuesPath = "/web/" + PublicationProtocol.FacetsPath;

    /// <summary>The header of a server page. It is the header with the Issues link on the server issues index.</summary>
    public static readonly IReadOnlyList<NavLink> PreviewHeader =
        [.. Header.Select(link => link.Section == PublicSection.Issues ? link with { Path = PreviewIssuesPath } : link)];

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
    /// The breadcrumb trail of an inner page. A page of one kind runs Home, the section of its kind, then its short name. Another page runs
    /// Home, its section, then its title. The section link is absent when it leads to the page itself.
    /// </summary>
    public static PublicTrail Trail(PublicSection? section, string title, string? canonicalPath, PublishedRecordKind? kind, string? shortName)
    {
        var home = new PublicBreadcrumbItem("Home", PublicationProtocol.HomePath);
        if (kind is { } recordKind) return new([home, RecordSection(recordKind)], shortName ?? title);

        var link = Header.Concat(Footer).Append(AllRecords).FirstOrDefault(candidate => section is not null && candidate.Section == section);
        if (link is null || link.Path == canonicalPath || IsBoardsPage(link, canonicalPath)) return new([home], title);
        return new([home, new(link.Text, link.Path)], title);
    }

    /// <summary>The header links the static board until the cutover, so the server boards page is the page of its own section link.</summary>
    private static bool IsBoardsPage(NavLink link, string? canonicalPath) => link.Section == PublicSection.Boards && canonicalPath == BoardsPath;

    /// <summary>The header section of a record page. A kind with no header link marks none.</summary>
    public static PublicSection? OfRecord(PublishedRecordKind kind) => kind switch
    {
        PublishedRecordKind.Vote => PublicSection.Votes,
        PublishedRecordKind.Action => PublicSection.Bills,
        PublishedRecordKind.ExecutiveAction => PublicSection.ExecutiveActions,
        _ => null
    };

    private static PublicBreadcrumbItem RecordSection(PublishedRecordKind kind) => kind switch
    {
        PublishedRecordKind.Brief => new(PublishedRecordLabels.KindPlural(kind), PublicationProtocol.BriefsPagePath(1)),
        PublishedRecordKind.Action => new("Bills", PublicationProtocol.SearchByKindPath(kind)),
        _ => new(PublishedRecordLabels.KindPlural(kind), PublicationProtocol.SearchByKindPath(kind))
    };
}
