using CivicLens.PublicApi.Records;
using CivicLens.PublicWeb.Publication;
using CivicLens.Publication.Contracts;

namespace CivicLens.PublicApi.Statements;

/// <summary>Reads everything that the server-rendered statement page shows: the statement detail with the term of each speaker, and the names of its speakers and issues. One snapshot holds both reads, so the page comes from one release.
/// The seat words are the words of the officials list, from <see cref="OfficialSeat"/>.</summary>
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
        var (names, issues) = await snapshot.ReadNamesAsync(transaction, row.SpeakerIds, row.IssueIds, cancellationToken);
        var speakers = names.Zip(detail.SpeakerTerms, (name, term) => name with
        {
            Seat = OfficialSeat.Text(term.Kind, term.Office, term.State, term.District),
            Party = term.Party
        });
        return new(detail.Release, row.RecordId, row.RevisionNumber, row.StatementDate, row.Headline, row.Summary, row.TextOrigin, row.Label, detail.ApprovedBy,
            [.. speakers], issues, detail.Source, detail.ArticleUrl, detail.StatementDateEvidence, detail.Quotations, detail.ApprovedAtUtc, detail.FirstPublishedAtUtc);
    }
}
