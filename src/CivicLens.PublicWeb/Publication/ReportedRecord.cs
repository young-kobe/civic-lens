using CivicLens.Publication.Contracts;

namespace CivicLens.PublicWeb.Publication;

/// <summary>The one record that a page shows. The footer report link names it and binds the report to its exact revision.</summary>
public sealed record ReportedRecord(string RecordId, int Revision, PublishedRecordKind Kind, string Title)
{
    public string ReportPath => PublicationProtocol.ReportPageLink(RecordId, Revision, Title);
}
