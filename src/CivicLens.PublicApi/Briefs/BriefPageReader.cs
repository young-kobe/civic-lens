using CivicLens.PublicWeb.Publication;

namespace CivicLens.PublicApi.Briefs;

/// <summary>Reads what the server-rendered brief page shows. The brief view holds the text and its citations, so one read of the store is one snapshot of one release.</summary>
public sealed class BriefPageReader(BriefStore briefs)
{
    /// <summary>Reads the page of one brief. Null means the active release holds no such brief.</summary>
    public async Task<BriefPageData?> ReadAsync(string recordId, CancellationToken cancellationToken)
    {
        if (await briefs.FindAsync(recordId, cancellationToken) is not { } detail) return null;
        var row = detail.Brief;
        return new(detail.Release, row.RecordId, row.RevisionNumber, row.WeekStart, row.Headline, row.TextOrigin, row.Label, detail.Paragraphs, detail.Citations,
            row.CutoffReleaseNumber, new() { Considered = row.CoverageConsidered, SentToModel = row.CoverageSentToModel, Cited = detail.Citations.Length }, row.ApprovedAtUtc, row.FirstPublishedAtUtc);
    }
}
