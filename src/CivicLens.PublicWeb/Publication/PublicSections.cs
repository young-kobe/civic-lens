using CivicLens.Publication.Contracts;

namespace CivicLens.PublicWeb.Publication;

public static class PublicSections
{
    /// <summary>One link of the header or footer. HiddenText is read by assistive technology after the link text.</summary>
    public sealed record NavLink(PublicSection? Section, string Text, string Path, string? HiddenText = null);

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

    /// <summary>The header of every page. Each kind link leads to the list page of its kind.</summary>
    public static readonly IReadOnlyList<NavLink> Header =
    [
        new(PublicSection.Officials, "Officials", PublicationProtocol.OfficialsPagePath),
        new(PublicSection.Issues, "Issues", PublicationProtocol.FacetsPath),
        new(PublicSection.Votes, "Votes", PublicationProtocol.ListPagePath(PublishedRecordKind.Vote)),
        new(PublicSection.Bills, "Bills", PublicationProtocol.ListPagePath(PublishedRecordKind.Action)),
        new(PublicSection.ExecutiveActions, "Executive actions", PublicationProtocol.ListPagePath(PublishedRecordKind.ExecutiveAction)),
        new(PublicSection.Boards, "Compare parties", PublicationProtocol.BoardsPagePath)
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
        PublishedRecordKind.ExecutiveAction => PublicSection.ExecutiveActions,
        _ => null
    };

    /// <summary>The section crumb of a record page. A kind with a list page links its list. A kind with none links the search page filtered by the kind.</summary>
    private static PublicBreadcrumbItem RecordSection(PublishedRecordKind kind) =>
        RecordLists.FirstOrDefault(list => list.Kind == kind) is { } list ? new(list.Label, list.Path)
        : kind == PublishedRecordKind.Brief ? new(PublishedRecordLabels.KindPlural(kind), PublicationProtocol.BriefsPagePath(1))
        : new(PublishedRecordLabels.KindPlural(kind), PublicationProtocol.SearchByKindPath(kind));
}
