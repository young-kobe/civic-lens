using CivicLens.Publication.Contracts;

namespace CivicLens.PublicWeb.Publication;

/// <summary>
/// The one record that a page shows. The footer report link names it and binds the report to its exact revision. The trail ends
/// with the short name when the page has one, and with the title when it has none.
/// </summary>
public sealed record ReportedRecord(string RecordId, int Revision, PublishedRecordKind Kind, string Title, string? ShortName = null)
{
    public string ReportPath => PublicationProtocol.ReportPageLink(RecordId, Revision, Title);

    public PublicSections.NavLink ReportLink => new(null, PublishedRecordLabels.ReportKindLink(Kind), ReportPath, Title);
}
