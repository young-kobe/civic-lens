using CivicLens.PublicApi.Records;
using CivicLens.PublicWeb.Publication;
using CivicLens.Publication.Contracts;

namespace CivicLens.PublicApi.Briefs;

/// <summary>Reads what the server-rendered brief page shows: the text and its citations from the brief view, and the corrections of the brief. One snapshot holds both reads, so the page comes from one release.</summary>
public sealed class BriefPageReader(BriefStore briefs, CorrectionHistoryStore corrections, RecordSnapshot snapshot)
{
    /// <summary>Reads the page of one brief. Null means the active release holds no such brief.</summary>
    public async Task<BriefPageData?> ReadAsync(string recordId, CancellationToken cancellationToken)
    {
        var (connection, transaction) = await snapshot.OpenAsync(cancellationToken);
        await using var _ = connection;
        await using var __ = transaction;
        if (await briefs.FindAsync(transaction, recordId, cancellationToken) is not { } detail) return null;
        var row = detail.Brief;
        var corrected = await corrections.ListAsync(transaction, PublishedRecordKind.Brief, recordId, row.RevisionNumber, cancellationToken);
        return new(detail.Release, row.RecordId, row.RevisionNumber, row.WeekStart, row.Headline, row.TextOrigin, row.Label, detail.Paragraphs, detail.Citations,
            row.CutoffReleaseNumber, new() { Considered = row.CoverageConsidered, SentToModel = row.CoverageSentToModel, Cited = detail.Citations.Length }, row.ApprovedAtUtc, row.FirstPublishedAtUtc)
        {
            Corrections = corrected
        };
    }
}
