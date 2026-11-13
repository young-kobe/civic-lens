using CivicLens.PublicApi.Records;
using CivicLens.PublicWeb.Publication;
using CivicLens.Publication.Contracts;

namespace CivicLens.PublicApi.DocumentChanges;

/// <summary>Reads everything that the server-rendered document change page shows: the change detail, the names of its officials and issues, and its corrections. One snapshot holds every read, so the page comes from one release.</summary>
public sealed class DocumentChangePageReader(DocumentChangeStore changes, CorrectionHistoryStore corrections, RecordSnapshot snapshot)
{
    /// <summary>Reads the page of one document change. Null means the active release holds no such record.</summary>
    public async Task<DocumentChangePageData?> ReadAsync(string recordId, CancellationToken cancellationToken)
    {
        var (connection, transaction) = await snapshot.OpenAsync(cancellationToken);
        await using var _ = connection;
        await using var __ = transaction;
        if ((await changes.FindAsync(transaction, recordId, cancellationToken)).Detail is not { } detail) return null;
        var row = detail.DocumentChange;
        var (officials, issues) = await snapshot.ReadNamesAsync(transaction, row.OfficialIds, row.IssueIds, cancellationToken);
        var corrected = await corrections.ListAsync(transaction, PublishedRecordKind.DocumentChange, recordId, row.RevisionNumber, cancellationToken);
        return new(detail.Release, row.RecordId, row.RevisionNumber, row.Headline, row.Summary, row.TextOrigin, row.Label, row.Significance, row.Limits,
            row.Institution, row.ChangeDate, officials, issues, detail.Before, detail.After, detail.Changes, detail.Citations, detail.ChangeDateEvidence)
        {
            Corrections = corrected
        };
    }
}
