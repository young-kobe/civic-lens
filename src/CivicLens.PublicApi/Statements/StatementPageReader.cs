using CivicLens.PublicApi.Records;
using CivicLens.PublicWeb.Publication;
using CivicLens.Publication.Contracts;

namespace CivicLens.PublicApi.Statements;

/// <summary>Reads everything that the server-rendered statement page shows: the statement detail and the names of its speakers and issues. One snapshot holds both reads, so the page comes from one release.</summary>
public sealed class StatementPageReader(StatementStore statements, RecordSnapshot snapshot)
{
    /// <summary>Reads the page of one statement. Null means the active release holds no such statement.</summary>
    public async Task<StatementPageData?> ReadAsync(string recordId, CancellationToken cancellationToken)
    {
        var (connection, transaction) = await snapshot.OpenAsync(cancellationToken);
        await using var _ = connection;
        await using var __ = transaction;
        if ((await statements.FindAsync(transaction, recordId, cancellationToken)).Detail is not { } detail) return null;
        var row = detail.Statement;
        var (speakers, issues) = await snapshot.ReadNamesAsync(transaction, row.SpeakerIds, row.IssueIds, cancellationToken);
        return new(detail.Release, row.RecordId, row.RevisionNumber, row.StatementDate, row.Headline, row.Summary, row.TextOrigin, row.Label, ApproverOf(row),
            speakers, issues, detail.Source, detail.ArticleUrl, detail.StatementDateEvidence, detail.Quotations);
    }

    /// <summary>The view has no approver column. It sets the label only for a model summary that code approved, so a label stands for code approval and no label for a person.</summary>
    private static PublishedApprover ApproverOf(StatementRow row) => row.Label is null ? PublishedApprover.Person : PublishedApprover.Code;
}
