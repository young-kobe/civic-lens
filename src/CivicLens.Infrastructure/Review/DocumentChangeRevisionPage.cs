using CivicLens.Core.Documents;
using CivicLens.Core.Review;
using CivicLens.Infrastructure.Review.Persistence;

namespace CivicLens.Infrastructure.Review;

/// <summary>Document change revision rows with their official, issue, and citation rows. A page costs a fixed number of queries.</summary>
internal sealed record DocumentChangeRevisionPage(IReadOnlyList<DocumentChangeRevisionRow> Rows,
    ILookup<(string DraftId, int RevisionNumber), DocumentChangeRevisionOfficialRow> Officials,
    ILookup<(string DraftId, int RevisionNumber), DocumentChangeRevisionIssueRow> Issues,
    ILookup<(string DraftId, int RevisionNumber), DocumentChangeRevisionCitationRow> Citations) : IRevisionRowPage<DocumentChangeRevisionRow>
{
    /// <summary>Rebuilds the revision of a row. A row with null columns fails here, and the JSON is no fallback.</summary>
    public DocumentChangeDraftRevision Rebuild(DocumentChangeRevisionRow row)
    {
        if (row.Headline is null)
            throw DocumentChangeRevisionRows.NotBackfilled($"Document change revision {row.DraftId} {row.RevisionNumber}");
        var key = (row.DraftId, row.RevisionNumber);
        return new(row.DraftId, row.RevisionNumber, row.ComparisonId!, row.AuthorSubject, new DateTimeOffset(row.CreatedAtUtcTicks, TimeSpan.Zero),
            row.Headline, row.Summary!, row.Significance, row.Limits, row.Institution!, row.ChangeDate,
            row.ChangeDateEvidenceExtractionId is null ? null : new SourceSpan(row.ChangeDateEvidenceExtractionId, row.ChangeDateEvidenceStart!.Value, row.ChangeDateEvidenceLength!.Value),
            [.. Officials[key].OrderBy(official => official.Position).Select(official => official.OfficialId)],
            [.. Issues[key].OrderBy(issue => issue.Position).Select(issue => issue.IssueId)],
            [.. Citations[key].OrderBy(citation => citation.Position)
                .Select(citation => new SourceSpan(citation.CitationExtractionId, citation.CitationStart, citation.CitationLength))]);
    }

    public IReadOnlyList<StoredRevision<DocumentChangeRevisionRow, DocumentChangeDraftRevision>> ToStoredRevisions() =>
        [.. Rows.Select(row => new StoredRevision<DocumentChangeRevisionRow, DocumentChangeDraftRevision>(row, () => Rebuild(row)))];
}
