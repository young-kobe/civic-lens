using CivicLens.PublicApi.Records;
using CivicLens.PublicWeb.Publication;

namespace CivicLens.PublicApi.DocumentChanges;

/// <summary>Reads everything that the server-rendered document change page shows: the change detail and the names of its officials and issues. One snapshot holds both reads, so the page comes from one release.</summary>
public sealed class DocumentChangePageReader(DocumentChangeStore changes, RecordNames names)
{
    /// <summary>Reads the page of one document change. Null means the active release holds no such record.</summary>
    public async Task<DocumentChangePageData?> ReadAsync(string recordId, CancellationToken cancellationToken)
    {
        var (connection, transaction) = await names.OpenSnapshotAsync(cancellationToken);
        await using var _ = connection;
        await using var __ = transaction;
        if ((await changes.FindAsync(transaction, recordId, cancellationToken)).Detail is not { } detail) return null;
        var row = detail.DocumentChange;
        var (officials, issues) = await names.ReadAsync(transaction, row.OfficialIds, row.IssueIds, cancellationToken);
        return new(detail.Release, row.RecordId, row.RevisionNumber, row.Headline, row.Summary, row.TextOrigin, row.Significance, row.Limits,
            row.Institution, row.ChangeDate, officials, issues, detail.Before, detail.After, detail.Changes, detail.Citations, detail.ChangeDateEvidence);
    }
}
