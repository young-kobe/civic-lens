using CivicLens.PublicApi.Records;
using CivicLens.PublicWeb.Publication;
using CivicLens.Publication.Contracts;

namespace CivicLens.PublicApi.Relationships;

/// <summary>Reads everything that the server-rendered relationship page shows: the relationship, the names of its speakers and issues, and its corrections.
/// One snapshot holds every read, so the page comes from one release. Precedent: <see cref="DocumentChanges.DocumentChangePageReader"/>.</summary>
public sealed class RelationshipPageReader(RelationshipStore relationships, CorrectionHistoryStore corrections, RecordSnapshot snapshot)
{
    /// <summary>Reads the page of one relationship. Null means the active release holds no such relationship with two active ends.</summary>
    public async Task<RelationshipPageData?> ReadAsync(string recordId, CancellationToken cancellationToken)
    {
        var (connection, transaction) = await snapshot.OpenAsync(cancellationToken);
        await using var _ = connection;
        await using var __ = transaction;
        if ((await relationships.FindAsync(transaction, recordId, cancellationToken)).Detail is not { } detail) return null;
        var (speakers, issues) = await snapshot.ReadNamesAsync(transaction, detail.OfficialIds, detail.IssueIds, cancellationToken);
        var corrected = await corrections.ListAsync(transaction, PublishedRecordKind.Relationship, recordId, detail.Page.RevisionNumber, cancellationToken);
        return detail.Page with { Speakers = speakers, Issues = issues, Corrections = corrected };
    }
}
