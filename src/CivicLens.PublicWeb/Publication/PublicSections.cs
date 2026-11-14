using CivicLens.Publication.Contracts;

namespace CivicLens.PublicWeb.Publication;

public static class PublicSections
{
    /// <summary>One link of the header or footer. HiddenText is read by assistive technology after the link text.
    /// A narrow-only link hides at 1280 px and wider, where the header shows a control that does the same job.</summary>
    public sealed record NavLink(PublicSection? Section, string Text, string Path, string? HiddenText = null, bool NarrowOnly = false);

    /// <summary>One per-kind list page. The label is the tab text of the list. <see cref="PublicationProtocol.ListPagePath"/> owns its path.</summary>
    public sealed record RecordList(PublishedRecordKind Kind, string Label)
    {
        public string Path => PublicationProtocol.ListPagePath(Kind);
    }

    /// <summary>The four per-kind list pages, in tab order. The one owner of their labels.</summary>
    public static readonly IReadOnlyList<RecordList> RecordLists =
    [
        new(PublishedRecordKind.Vote, "Votes"),
        new(PublishedRecordKind.Action, "Bills"),
        new(PublishedRecordKind.Statement, "Statements"),
        new(PublishedRecordKind.ExecutiveAction, "Executive actions")
    ];

    public static RecordList ListOf(PublishedRecordKind kind) => RecordLists.Single(list => list.Kind == kind);

    /// <summary>The header of every page and of its Menu sheet, in the order of the mockup. Each kind link leads to the list page of its kind.
    /// Search comes last, because the mockup holds no Search link and a link after the sections keeps their approved order.
    /// Search is narrow-only, because the header search box shows at 1280 px and wider.</summary>
    public static readonly IReadOnlyList<NavLink> Header =
    [
        new(PublicSection.Officials, "Officials", PublicationProtocol.OfficialsPagePath),
        new(PublicSection.Issues, "Issues", PublicationProtocol.FacetsPath),
        new(PublicSection.Votes, "Votes", PublicationProtocol.ListPagePath(PublishedRecordKind.Vote)),
        new(PublicSection.Bills, "Bills", PublicationProtocol.ListPagePath(PublishedRecordKind.Action)),
        new(PublicSection.Statements, "Statements", PublicationProtocol.ListPagePath(PublishedRecordKind.Statement)),
        new(PublicSection.ExecutiveActions, "Executive actions", PublicationProtocol.ListPagePath(PublishedRecordKind.ExecutiveAction)),
        new(PublicSection.Boards, "Compare parties", PublicationProtocol.BoardsPagePath),
        new(PublicSection.Search, "Search", PublicationProtocol.SearchPagePath, NarrowOnly: true)
    ];

    /// <summary>The header link of the section that holds a public path: the link of the page itself, or of the folder that holds its pages,
    /// such as <c>officials/{id}.html</c> under Officials. Null when no header section holds the path, such as a record page.</summary>
    public static NavLink? SectionOf(string publicPath) => Header.FirstOrDefault(link =>
        publicPath == link.Path || publicPath.StartsWith(System.IO.Path.GetFileNameWithoutExtension(link.Path) + "/", StringComparison.Ordinal));

    /// <summary>The old all records list. It is outside the header, and it names its own page in the trail.</summary>
    private static readonly NavLink AllRecords = new(PublicSection.AllRecords, "Votes and bills", PublicationProtocol.AllRecordsPagePath(1));

    public static readonly NavLink Report = new(null, $"{PublishedRecordLabels.ReportLinkStart} page", PublicationProtocol.ReportPagePath);

    /// <summary>The footer link of the method page. Its text is the title of the page.</summary>
    public static readonly NavLink Method = new(null, "How the counts work", PublicationProtocol.MethodPagePath);

    public static readonly IReadOnlyList<NavLink> Footer =
    [
        Method,
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
        if (link is null || link.Path == canonicalPath) return new([home], title);
        return new([home, new(link.Text, link.Path)], title);
    }

    /// <summary>The header section of a record page. A kind with no header link marks none.</summary>
    public static PublicSection? OfRecord(PublishedRecordKind kind) => kind switch
    {
        PublishedRecordKind.Vote => PublicSection.Votes,
        PublishedRecordKind.Action => PublicSection.Bills,
        PublishedRecordKind.Statement => PublicSection.Statements,
        PublishedRecordKind.ExecutiveAction => PublicSection.ExecutiveActions,
        _ => null
    };

    /// <summary>The section crumb of a record page. A kind with a list page links its list. A kind with none links the search page filtered by the kind.</summary>
    private static PublicBreadcrumbItem RecordSection(PublishedRecordKind kind) =>
        RecordLists.FirstOrDefault(list => list.Kind == kind) is { } list ? new(list.Label, list.Path)
        : kind == PublishedRecordKind.Brief ? new(PublishedRecordLabels.KindPlural(kind), PublicationProtocol.BriefsPagePath(1))
        : new(PublishedRecordLabels.KindPlural(kind), PublicationProtocol.SearchByKindPath(kind));
}
