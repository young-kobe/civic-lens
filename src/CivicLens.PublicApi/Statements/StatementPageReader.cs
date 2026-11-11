using CivicLens.PublicApi.Records;
using CivicLens.PublicWeb.Publication;

namespace CivicLens.PublicApi.Statements;

/// <summary>Reads everything that the server-rendered statement page shows: the statement detail and the names of its speakers and issues. One snapshot holds both reads, so the page comes from one release.</summary>
public sealed class StatementPageReader(StatementStore statements, RecordNames names)
{
    /// <summary>Reads the page of one statement. Null means the active release holds no such statement.</summary>
    public async Task<StatementPageData?> ReadAsync(string recordId, CancellationToken cancellationToken)
    {
        var (connection, transaction) = await names.OpenSnapshotAsync(cancellationToken);
        await using var _ = connection;
        await using var __ = transaction;
        if ((await statements.FindAsync(transaction, recordId, cancellationToken)).Detail is not { } detail) return null;
        var row = detail.Statement;
        var (speakers, issues) = await names.ReadAsync(transaction, row.SpeakerIds, row.IssueIds, cancellationToken);
        return new(detail.Release, row.RecordId, row.RevisionNumber, row.StatementDate, row.Headline, row.Summary, row.TextOrigin, row.Label,
            speakers, issues, detail.Source, detail.ArticleUrl, detail.StatementDateEvidence, detail.Quotations);
    }
}
